import { test } from "node:test";
import assert from "node:assert/strict";
import type { FeatureFlag, FlagAudience, FlagCondition } from "../../api/types.ts";
import {
  addCondition,
  canSave,
  isCustom,
  loadEditor,
  removeCondition,
  saveBlocker,
  savePayload,
  setConditionParameters,
  setMatch,
  setMode,
} from "./flag-editor-state.ts";

const allowed = { allowed: true, reason: null };

type Over = {
  enabled?: boolean;
  audienceDetail?: FlagAudience | null;
  state?: FeatureFlag["state"];
  actions?: Partial<FeatureFlag["actions"]>;
  conditions?: Array<FlagCondition>;
  match?: "any" | "all";
};

function flag(over: Over = {}): FeatureFlag {
  return {
    name: "Checkout.NewFlow",
    isDeclared: true,
    declaredIn: "configuration",
    declaredTwice: false,
    section: "Checkout",
    description: null,
    tags: [],
    isPublic: false,
    state: over.state ?? "off",
    defaultState: "off",
    settings: { enabled: over.enabled ?? true, conditions: over.conditions ?? [], match: over.match ?? "any" },
    defaultSettings: null,
    override: null,
    differsFromDefault: false,
    actions: { switch: allowed, save: allowed, reset: allowed, remove: allowed, ...over.actions },
    summary: null,
    enabled: over.enabled ?? true,
    audience: null,
    audienceDetail: over.audienceDetail ?? null,
  };
}

const targeting = (members: Array<string>, groups: Array<string>): FlagCondition => ({
  name: "Microsoft.Targeting",
  parameters: {
    Audience: { Users: members, Groups: groups.map((Name) => ({ Name, RolloutPercentage: 100 })), DefaultRolloutPercentage: 0 },
  },
});
const culture: FlagCondition = { name: "Contoso.Culture", parameters: { Cultures: ["fr"] } };

const window: FlagCondition = { name: "Microsoft.TimeWindow", parameters: { Start: "2026-01-01T00:00:00Z" } };
const root: FlagCondition = { name: "Umbricians.Root", parameters: { Roots: ["r1"] } };
const editors = ["Microsoft.Targeting", "Microsoft.TimeWindow", "Contoso.Culture", "Umbricians.Root"];

test("a flag that is off but keeps conditions opens off, with those conditions", () => {
  const state = loadEditor(flag({ enabled: false, conditions: [culture] }));
  assert.equal(state.mode, "off");
  assert.deepEqual(state.conditions, [culture]);
  assert.equal(state.dirty, false);
});

test("a flag on for everyone opens as everyone, and one with conditions as conditions", () => {
  assert.equal(loadEditor(flag()).mode, "everyone");
  assert.equal(loadEditor(flag({ conditions: [culture] })).mode, "conditions");
});

test("switching a flag off and saving keeps its conditions", () => {
  const state = setMode(loadEditor(flag({ conditions: [targeting(["m1"], ["g"])] })), "off");
  assert.deepEqual(savePayload(state), { enabled: false, conditions: [targeting(["m1"], ["g"])], match: "any" });
  assert.deepEqual(savePayload(setMode(state, "conditions")).conditions, savePayload(state).conditions);
  assert.equal(savePayload(setMode(state, "conditions")).enabled, true);
});

test("switching to everyone saves no conditions, but switching back restores them in the editor", () => {
  const state = setMode(loadEditor(flag({ conditions: [culture] })), "everyone");
  assert.deepEqual(savePayload(state), { enabled: true, conditions: [], match: "any" });
  assert.deepEqual(setMode(state, "conditions").conditions, [culture]);
  assert.equal(setMode(state, "conditions").dirty, false);
});

test("an off flag with nothing kept saves no conditions", () => {
  assert.deepEqual(savePayload(loadEditor(flag({ enabled: false }))), { enabled: false, conditions: [], match: "any" });
});

test("adding a condition switches to on with conditions and saves it with the conditions it already had", () => {
  const state = addCondition(loadEditor(flag({ conditions: [culture] })), "Microsoft.Targeting");
  assert.equal(state.mode, "conditions");
  assert.deepEqual(savePayload(state).conditions, [culture, targeting([], [])]);
  assert.equal(state.dirty, true);
});

test("adding a condition to a flag that is on for everyone starts from the condition's own parameters", () => {
  const state = addCondition(loadEditor(flag()), "Contoso.Culture");
  assert.deepEqual(savePayload(state), { enabled: true, conditions: [{ name: "Contoso.Culture", parameters: {} }], match: "any" });
});

test("a condition is added once, even when the flag writes its name short", () => {
  const loaded = loadEditor(flag({ conditions: [{ name: "targeting", parameters: {} }] }));
  assert.equal(addCondition(loaded, "Microsoft.Targeting").conditions.length, 1);
});

test("removing a condition drops it, and removing them all leaves nothing to save as conditions", () => {
  const loaded = loadEditor(flag({ conditions: [culture, window], match: "all" }));
  const removed = removeCondition(loaded, 0);
  assert.deepEqual(removed.conditions, [window]);
  assert.equal(removed.dirty, true);
  assert.deepEqual(saveBlocker(removeCondition(removed, 0), editors), { kind: "noConditions" });
});

test("adding then removing a condition is no change", () => {
  const loaded = loadEditor(flag({ conditions: [culture] }));
  const there = addCondition(loaded, "Umbricians.Root");
  assert.equal(removeCondition(there, 1).dirty, false);
});

test("Any and All: the match is saved, and changing it is a change only with two conditions or more", () => {
  const two = loadEditor(flag({ conditions: [culture, window] }));
  const all = setMatch(two, "all");
  assert.equal(all.dirty, true);
  assert.equal(savePayload(all).match, "all");
  assert.equal(setMatch(all, "any").dirty, false);

  const one = loadEditor(flag({ conditions: [culture] }));
  assert.equal(setMatch(one, "all").dirty, false, "a single condition has nothing to match");
});

test("adding Root to a flag that has another condition switches to All, and says so until the match is chosen", () => {
  const state = addCondition(loadEditor(flag({ conditions: [culture] })), "Umbricians.Root");
  assert.equal(state.match, "all");
  assert.equal(state.matchSwitchedForRoot, true);
  assert.equal(savePayload(state).match, "all");

  const changedBack = setMatch(state, "any");
  assert.equal(changedBack.match, "any");
  assert.equal(changedBack.matchSwitchedForRoot, false);
});

test("Root alone, or a condition other than Root, leaves the match as it was", () => {
  assert.equal(addCondition(loadEditor(flag({ enabled: false })), "Umbricians.Root").match, "any");
  const other = addCondition(loadEditor(flag({ conditions: [culture] })), "Microsoft.Targeting");
  assert.equal(other.match, "any");
  assert.equal(other.matchSwitchedForRoot, false);
});

test("removing Root withdraws the notice", () => {
  const state = addCondition(loadEditor(flag({ conditions: [culture] })), "Umbricians.Root");
  assert.equal(removeCondition(state, 1).matchSwitchedForRoot, false);
  assert.equal(removeCondition(state, 0).matchSwitchedForRoot, true);
});

test("a condition with no editor blocks saving unless the flag goes on for everyone", () => {
  const loaded = loadEditor(flag({ conditions: [culture, window] }));
  const edited = setMatch(loaded, "all");
  assert.deepEqual(saveBlocker(edited, ["Contoso.Culture"]), { kind: "noEditor", names: ["Microsoft.TimeWindow"] });
  assert.equal(canSave(edited, true, ["Contoso.Culture"]), false);
  assert.equal(canSave(edited, true, editors.concat("Microsoft.TimeWindow")), true);
  assert.equal(canSave(setMode(edited, "everyone"), true, ["Contoso.Culture"]), true);
});

test("editors are found the way Microsoft finds filters: ignoring case and by short name", () => {
  const loaded = loadEditor(flag({ conditions: [{ name: "targeting", parameters: { Audience: { Users: ["m1"] } } }, { name: "CULTURE", parameters: {} }] }));
  assert.equal(saveBlocker(setMatch(loaded, "all"), editors), null);
});

test("an audience that names nobody is blocked, unless it gives a percentage of everyone", () => {
  const nobody = setMode(loadEditor(flag({ enabled: false })), "conditions");
  const added = addCondition(nobody, "Microsoft.Targeting");
  assert.deepEqual(saveBlocker(added, ["Microsoft.Targeting"]), { kind: "nobody" });
  const ten = setConditionParameters(added, 0, { Audience: { DefaultRolloutPercentage: 10 } });
  assert.equal(saveBlocker(ten, ["Microsoft.Targeting"]), null);
  const member = setConditionParameters(added, 0, { Audience: { Users: ["m1"] } });
  assert.equal(saveBlocker(member, ["Microsoft.Targeting"]), null);
  assert.equal(saveBlocker(setMode(added, "off"), ["Microsoft.Targeting"]), null);
});

test("on with conditions and none added is blocked", () => {
  const state = setMode(loadEditor(flag({ enabled: false })), "conditions");
  assert.deepEqual(saveBlocker(state, editors), { kind: "noConditions" });
});

test("a custom flag is one the editor can't read", () => {
  assert.equal(isCustom(flag({ state: "custom" })), true);
  assert.equal(isCustom(flag()), false);
});

test("editing parameters marks the editor dirty, and undoing the edit clears it", () => {
  const loaded = loadEditor(flag({ conditions: [targeting(["m1", "m2"], ["g1"])] }));
  const edited = setConditionParameters(loaded, 0, targeting(["m1", "m3"], ["g1"]).parameters);
  assert.equal(edited.dirty, true);
  assert.equal(setConditionParameters(edited, 0, targeting(["m2", "m1"], ["g1"]).parameters).dirty, false, "values compare as sets");
  assert.equal(setMode(loaded, "off").dirty, true);
  assert.equal(loaded.dirty, false, "the loaded state is untouched");
});

test("reordering the values of a developer's condition is a change, because the server compares them in order", () => {
  const loaded = loadEditor(flag({ conditions: [{ name: "Contoso.Culture", parameters: { Cultures: ["fr", "de"] } }] }));
  const reordered = setConditionParameters(loaded, 0, { Cultures: ["de", "fr"] });
  assert.equal(reordered.dirty, true);
  assert.equal(setConditionParameters(reordered, 0, { Cultures: ["fr", "de"] }).dirty, false);
});

test("saving keeps every condition it did not edit, and how they match", () => {
  const loaded = loadEditor(flag({ enabled: false, conditions: [targeting(["m1"], []), culture, window], match: "all" }));
  const payload = savePayload(setConditionParameters(loaded, 0, targeting(["m9"], []).parameters));
  assert.deepEqual(payload.conditions, [targeting(["m9"], []), culture, window]);
  assert.equal(payload.match, "all");
  assert.equal(payload.enabled, false);
});

test("a flag loaded and saved without edits round trips what the server sent", () => {
  const conditions = [targeting(["m1"], ["g"]), culture, root];
  assert.deepEqual(savePayload(loadEditor(flag({ conditions, match: "all" }))), { enabled: true, conditions, match: "all" });
});

test("cannot save when the catalog refuses, when the editor can't manage flags, or when nothing changed", () => {
  const edit = (f: FeatureFlag) => setMode(loadEditor(f), "off");
  assert.equal(canSave(edit(flag()), true, editors), true);
  assert.equal(canSave(edit(flag({ actions: { save: { allowed: false, reason: "It has a time window." } } })), true, editors), false);
  assert.equal(canSave(edit(flag()), false, editors), false);
  assert.equal(canSave(loadEditor(flag()), true, editors), false);
});

test("an audience and a time window together: Match is Any until it is changed to All, and both are saved", () => {
  const audience = targeting(["m1"], []);
  const loaded = loadEditor(flag({ conditions: [audience] }));
  const added = addCondition(loaded, "Microsoft.TimeWindow");

  assert.deepEqual(added.conditions.map((condition) => condition.name), ["Microsoft.Targeting", "Microsoft.TimeWindow"]);
  assert.equal(added.match, "any");
  assert.equal(addCondition(added, "Microsoft.TimeWindow").conditions.length, 2, "a window is offered once");

  const windowed = setConditionParameters(added, 1, { Start: "2026-12-01T09:00:00Z" });
  const all = setMatch(windowed, "all");
  assert.deepEqual(savePayload(all), {
    enabled: true,
    conditions: [audience, { name: "Microsoft.TimeWindow", parameters: { Start: "2026-12-01T09:00:00Z" } }],
    match: "all",
  });
  assert.equal(saveBlocker(all, editors), null);
});

test("adding Root beside the audience switches to All, and the saved flag keeps both conditions", () => {
  const state = addCondition(loadEditor(flag({ conditions: [targeting(["m1"], [])] })), "Umbricians.Root");
  assert.equal(state.match, "all");
  assert.equal(state.matchSwitchedForRoot, true);
  assert.deepEqual(
    savePayload(state).conditions.map((condition) => condition.name),
    ["Microsoft.Targeting", "Umbricians.Root"],
  );
});

const withCondition = (name: string, parameters: Record<string, unknown>) =>
  setConditionParameters(addCondition(setMode(loadEditor(flag({ enabled: false })), "conditions"), name), 0, parameters);

test("a time window with no start or end is blocked until one is chosen", () => {
  const editors = ["Microsoft.TimeWindow"];
  assert.deepEqual(saveBlocker(withCondition("Microsoft.TimeWindow", {}), editors), { kind: "timeWindow", problem: "neither" });
  assert.equal(saveBlocker(withCondition("Microsoft.TimeWindow", { Start: "2026-12-01T09:00:00Z" }), editors), null);
  assert.equal(saveBlocker(withCondition("Microsoft.TimeWindow", { End: "2026-12-01T09:00:00Z" }), editors), null);
});

test("a time window that ends before it starts, or holds an unreadable time, is blocked", () => {
  const editors = ["Microsoft.TimeWindow"];
  const backwards = { Start: "2026-12-02T09:00:00Z", End: "2026-12-01T09:00:00Z" };
  assert.deepEqual(saveBlocker(withCondition("Microsoft.TimeWindow", backwards), editors), { kind: "timeWindow", problem: "startNotBeforeEnd" });
  assert.deepEqual(saveBlocker(withCondition("Microsoft.TimeWindow", { Start: "soon" }), editors), { kind: "timeWindow", problem: "unreadableStart" });
});

test("a Root condition that names no site is blocked until one is chosen", () => {
  const editors = ["Umbricians.Root"];
  assert.deepEqual(saveBlocker(withCondition("Umbricians.Root", { Roots: [] }), editors), { kind: "noSites" });
  assert.equal(saveBlocker(withCondition("Umbricians.Root", { Roots: ["abc"] }), editors), null);
});

test("an incomplete time window or Root condition doesn't block a flag that is off or on for everyone", () => {
  const editors = ["Microsoft.TimeWindow", "Umbricians.Root"];
  const window = withCondition("Microsoft.TimeWindow", {});
  assert.equal(saveBlocker(setMode(window, "off"), editors), null);
  assert.equal(saveBlocker(setMode(window, "everyone"), editors), null);
});
