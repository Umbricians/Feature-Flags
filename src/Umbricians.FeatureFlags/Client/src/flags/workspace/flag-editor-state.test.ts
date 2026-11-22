import { test } from "node:test";
import assert from "node:assert/strict";
import type { FeatureFlag, FlagAudience, FlagCondition, ProviderGroup } from "../../api/types.ts";
import {
  canSave,
  hasOtherConditions,
  loadEditor,
  removeGroup,
  savePayload,
  setMemberGroupKeys,
  setMembers,
  setMode,
  setProviderGroup,
} from "./flag-editor-state.ts";

const allowed = { allowed: true, reason: null };

type Over = {
  enabled?: boolean;
  audienceDetail?: FlagAudience | null;
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
    state: "off",
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

const kept: FlagAudience = {
  members: ["m1", "m2"],
  groups: [
    { id: "key-1", kind: "memberGroup", name: "Premium members" },
    { id: "VIPs", kind: "namedMemberGroup", name: "VIPs" },
    { id: "crm:gone", kind: "unknown", name: null },
  ],
};
const crm: ProviderGroup = { id: "crm:premium", name: "Premium in the CRM", description: null };

test("a flag that is off but keeps an audience opens off, with that audience", () => {
  const state = loadEditor(flag({ enabled: false, audienceDetail: kept }));
  assert.equal(state.mode, "off");
  assert.deepEqual(state.audience, kept);
  assert.equal(state.dirty, false);
});

test("a flag on for everyone opens as everyone, and one with an audience as audience", () => {
  const everyone = loadEditor(flag());
  assert.equal(everyone.mode, "everyone");
  assert.deepEqual(everyone.audience, { members: [], groups: [] });
  assert.equal(loadEditor(flag({ audienceDetail: kept })).mode, "audience");
});

test("switching a flag off and saving keeps who it was for, as a targeting condition", () => {
  const state = setMode(loadEditor(flag({ audienceDetail: kept })), "off");
  assert.deepEqual(savePayload(state), {
    enabled: false,
    conditions: [targeting(["m1", "m2"], ["key-1", "VIPs", "crm:gone"])],
    match: "any",
  });
  assert.deepEqual(savePayload(setMode(state, "audience")).conditions, savePayload(state).conditions);
  assert.equal(savePayload(setMode(state, "audience")).enabled, true);
});

test("switching to everyone saves no conditions", () => {
  const state = setMode(loadEditor(flag({ audienceDetail: kept })), "everyone");
  assert.deepEqual(savePayload(state), { enabled: true, conditions: [], match: "any" });
});

test("an off flag with nothing kept saves no conditions", () => {
  assert.deepEqual(savePayload(loadEditor(flag({ enabled: false }))), { enabled: false, conditions: [], match: "any" });
  assert.deepEqual(savePayload(setMode(loadEditor(flag()), "off")), { enabled: false, conditions: [], match: "any" });
});

test("an off flag whose audience was emptied saves the empty audience", () => {
  const state = setMembers(removeGroup(removeGroup(removeGroup(loadEditor(flag({ enabled: false, audienceDetail: kept })), "key-1"), "VIPs"), "crm:gone"), []);
  assert.deepEqual(savePayload(state), { enabled: false, conditions: [targeting([], [])], match: "any" });
});

test("saving keeps every condition it does not edit, and how they match", () => {
  const window: FlagCondition = { name: "Microsoft.TimeWindow", parameters: { Start: "2026-01-01T00:00:00Z" } };
  const loaded = loadEditor(flag({ enabled: false, audienceDetail: kept, conditions: [targeting(["m1", "m2"], []), culture, window], match: "all" }));

  const payload = savePayload(setMembers(loaded, ["m9"]));

  assert.deepEqual(payload.conditions, [targeting(["m9"], ["key-1", "VIPs", "crm:gone"]), culture, window]);
  assert.equal(payload.match, "all");
  assert.equal(payload.enabled, false);
});

test("a switched-off flag with conditions of its own is one the editor can't change here", () => {
  assert.equal(hasOtherConditions(flag({ enabled: false, conditions: [culture] })), true);
  assert.equal(hasOtherConditions(flag({ enabled: false, audienceDetail: kept, conditions: [targeting(["m1"], [])] })), false);
  assert.equal(hasOtherConditions(flag()), false);
});

test("editing marks the editor dirty, and undoing the edit clears it", () => {
  const loaded = loadEditor(flag({ audienceDetail: kept }));
  const mode = setMode(loaded, "everyone");
  assert.equal(mode.dirty, true);
  assert.equal(setMode(mode, "audience").dirty, false);

  const members = setMembers(loaded, ["m2", "m3"]);
  assert.equal(members.dirty, true);
  assert.equal(setMembers(members, ["m2", "m1"]).dirty, false, "members compare as a set");

  const added = setProviderGroup(loaded, crm, true);
  assert.equal(added.dirty, true);
  assert.equal(setProviderGroup(added, crm, false).dirty, false);
  assert.equal(setProviderGroup(added, crm, true).audience, added.audience, "a group is added once");

  const removed = removeGroup(loaded, "VIPs");
  assert.equal(removed.dirty, true);
  assert.equal(setMemberGroupKeys(removed, ["key-1"]).dirty, true);
  assert.equal(setMemberGroupKeys(loaded, ["key-1"]).dirty, false, "an unchanged picker is no change");
  assert.equal(loaded.dirty, false, "the loaded state is untouched");
});

test("picking member groups keeps member groups named in configuration and unknown ids, and saves their ids", () => {
  const state = setMemberGroupKeys(loadEditor(flag({ audienceDetail: kept })), ["key-2"]);
  assert.deepEqual(
    state.audience.groups.map((g) => [g.id, g.kind]),
    [["VIPs", "namedMemberGroup"], ["crm:gone", "unknown"], ["key-2", "memberGroup"]],
  );
  assert.deepEqual(savePayload(state).conditions, [targeting([ "m1", "m2"], ["VIPs", "crm:gone", "key-2"])]);
});

test("cannot save when the catalog refuses, when the editor can't manage flags, or when nothing changed", () => {
  const edit = (f: FeatureFlag) => setMode(loadEditor(f), "off");
  assert.equal(canSave(edit(flag()), true), true);
  assert.equal(canSave(edit(flag({ actions: { save: { allowed: false, reason: "It has a time window." } } })), true), false);
  assert.equal(canSave(edit(flag()), false), false);
  assert.equal(canSave(loadEditor(flag()), true), false);
});
