import { test } from "node:test";
import assert from "node:assert/strict";
import type { FlagActions } from "../api/types.ts";
import { ALL_SECTIONS, audienceParts, excludedParts, joinList, mayDo, quickAction, sectionFromOption, sectionOption, sections, shortName } from "./display.ts";

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
