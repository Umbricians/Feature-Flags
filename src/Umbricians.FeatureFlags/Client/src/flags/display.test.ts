import { test } from "node:test";
import assert from "node:assert/strict";
import type { FlagActions } from "../api/types.ts";
import { ALL_SECTIONS, audienceParts, conditionsOnSwitchOn, listNote, excludedParts, joinList, mayDo, quickAction, sectionFromOption, sectionOption, sections, shortName } from "./display.ts";

test("the section is taken off the front of the name", () => {
  assert.equal(shortName({ name: "Checkout.NewFlow", section: "Checkout" }), "NewFlow");
  assert.equal(shortName({ name: "checkout.NewFlow", section: "Checkout" }), "NewFlow");
  assert.equal(shortName({ name: "GiftCards", section: "Payments" }), "GiftCards");
  assert.equal(shortName({ name: "DarkMode", section: null }), "DarkMode");
  assert.equal(shortName({ name: "Checkout.", section: "Checkout" }), "Checkout.");
});

test("a page is split into its sections, in order", () => {
  const items = [{ section: "A" }, { section: "A" }, { section: "B" }, { section: null }];
  assert.deepEqual(
    sections(items, "name").map((s) => [s.name, s.items.length]),
    [["A", 2], ["B", 1], [null, 1]],
  );
});

test("sorted by last change, a page stays in one section", () => {
  assert.equal(sections([{ section: "A" }, { section: "B" }], "updated").length, 1);
  assert.equal(sections([], "updated").length, 0);
});

const allowed = { allowed: true, reason: null };
const refused = (reason: string) => ({ allowed: false, reason });
const actions = (over: Partial<FlagActions> = {}): FlagActions => ({
  switch: allowed,
  save: allowed,
  reset: allowed,
  remove: refused("A declared flag can't be removed."),
  ...over,
});

test("the catalog's refusal and its reason come before the permission", () => {
  const item = { actions: actions({ save: refused("It has a time window.") }) };
  assert.deepEqual(mayDo(item, "save", true), { allowed: false, reason: "It has a time window.", needsManage: false });
  assert.deepEqual(mayDo(item, "save", false), { allowed: false, reason: "It has a time window.", needsManage: false });
  assert.deepEqual(mayDo(item, "switch", true), { allowed: true, reason: null, needsManage: false });
});

test("an editor without Change feature flags is told they need it", () => {
  const item = { actions: actions() };
  assert.deepEqual(mayDo(item, "switch", false), { allowed: false, reason: null, needsManage: true });
  assert.deepEqual(mayDo(item, "reset", false), { allowed: false, reason: null, needsManage: true });
});

test("an undeclared flag's quick action is Remove, refused on a read-only site", () => {
  const undeclared = { isDeclared: false, actions: actions({ switch: refused("Undeclared."), remove: allowed }) };
  assert.deepEqual(quickAction(undeclared, true), { kind: "remove", allowed: true, reason: null, needsManage: false });
  assert.equal(quickAction(undeclared, false).needsManage, true);

  const readOnly = { isDeclared: false, actions: actions({ remove: refused("Read-only.") }) };
  assert.deepEqual(quickAction(readOnly, true), { kind: "remove", allowed: false, reason: "Read-only.", needsManage: false });

  assert.equal(quickAction({ isDeclared: true, actions: actions() }, true).kind, "switch");
});

test("an audience lists only the parts with anyone in them", () => {
  assert.deepEqual(audienceParts({ members: 3, memberGroups: 0, groups: 1, defaultPercentage: 0, excludedMembers: 0, excludedGroups: 0 }), [
    { kind: "members", count: 3 },
    { kind: "groups", count: 1 },
  ]);
  assert.deepEqual(audienceParts(null), []);
});

test("what an audience leaves out lists only the parts with anyone in them", () => {
  assert.deepEqual(excludedParts({ members: 0, memberGroups: 0, groups: 0, defaultPercentage: 0, excludedMembers: 2, excludedGroups: 0 }), [{ kind: "members", count: 2 }]);
  assert.deepEqual(excludedParts(null), []);
});

test("lists read as prose", () => {
  assert.equal(joinList([], "and"), "");
  assert.equal(joinList(["a"], "and"), "a");
  assert.equal(joinList(["a", "b"], "and"), "a and b");
  assert.equal(joinList(["a", "b", "c"], "and"), "a, b and c");
});

test("\"all sections\" is its own option and means no section filter", () => {
  assert.equal(sectionOption(undefined), ALL_SECTIONS);
  assert.equal(sectionOption(null), ALL_SECTIONS);
  assert.equal(sectionOption(""), "");
  assert.equal(sectionOption("Checkout"), "Checkout");
  assert.equal(sectionFromOption(ALL_SECTIONS), null);
  assert.equal(sectionFromOption(""), "");
  assert.equal(sectionFromOption("Checkout"), "Checkout");
});

test("switching on a flag that keeps conditions describes them, and one without says nothing of conditions", () => {
  const settings = (conditions: Array<{ name: string; parameters: Record<string, unknown> }>) => ({ enabled: false, conditions, match: "any" as const });
  const window = [{ name: "Microsoft.TimeWindow", parameters: {} }];
  assert.equal(conditionsOnSwitchOn({ settings: settings(window), summary: "From 1 Dec" }), "From 1 Dec");
  assert.equal(conditionsOnSwitchOn({ settings: settings(window), summary: null }), "");
  assert.equal(conditionsOnSwitchOn({ settings: settings([]), summary: null }), null);
  assert.equal(conditionsOnSwitchOn({ settings: null, summary: null }), null);
});

const note = (state: "off" | "everyone" | "audience" | "conditions" | "custom", audience: boolean, summary: string | null, conditions = 1) => ({
  state,
  audience: audience ? { members: 1, memberGroups: 0, groups: 0, defaultPercentage: 0, excludedMembers: 0, excludedGroups: 0 } : null,
  summary,
  settings: { enabled: state !== "off", conditions: Array.from({ length: conditions }, () => ({ name: "X", parameters: {} })), match: "any" as const },
});

test("the list notes a conditions flag by its summary, so a time window shows", () => {
  assert.deepEqual(listNote(note("conditions", false, "From 1 Dec")), { kind: "summary", text: "From 1 Dec" });
  assert.deepEqual(listNote(note("conditions", true, "1 member; From 1 Dec")), { kind: "summary", text: "1 member; From 1 Dec" });
});

test("the list notes an audience flag by its audience, and a flag that is off by what it keeps", () => {
  assert.deepEqual(listNote(note("audience", true, "1 member")), { kind: "audience" });
  assert.deepEqual(listNote(note("off", true, "1 member")), { kind: "audience" });
  assert.deepEqual(listNote(note("off", false, "From 1 Dec")), { kind: "summary", text: "From 1 Dec" });
});

test("the list has no note for a flag with nothing to say", () => {
  assert.equal(listNote(note("everyone", false, null, 0)), null);
  assert.equal(listNote(note("conditions", false, null)), null);
});
