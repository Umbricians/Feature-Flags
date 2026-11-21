import { test } from "node:test";
import assert from "node:assert/strict";
import type { FeatureFlag, FlagAudience, ProviderGroup } from "../../api/types.ts";
import {
  canSave,
  loadEditor,
  removeGroup,
  savePayload,
  setMemberGroupKeys,
  setMembers,
  setMode,
  setProviderGroup,
} from "./flag-editor-state.ts";

const allowed = { allowed: true, reason: null };

function flag(over: { enabled?: boolean; audienceDetail?: FlagAudience | null; actions?: Partial<FeatureFlag["flag"]["actions"]> } = {}): FeatureFlag {
  return {
    flag: {
      name: "Checkout.NewFlow",
      description: null,
      section: "Checkout",
      tags: [],
      declaredIn: "configuration",
      declaredTwice: false,
      isDeclared: true,
      state: "off",
      enabled: over.enabled ?? true,
      audience: null,
      hasOverride: true,
      differsFromDefault: false,
      actions: { switch: allowed, save: allowed, reset: allowed, remove: allowed, ...over.actions },
      updatedBy: null,
      updatedUtc: null,
    },
    defaultState: "off",
    audienceDetail: over.audienceDetail ?? null,
    overrideDefinition: null,
  };
}

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

test("switching a flag off and saving keeps who it was for", () => {
  const state = setMode(loadEditor(flag({ audienceDetail: kept })), "off");
  assert.deepEqual(savePayload(state), {
    enabled: false,
    audience: { members: ["m1", "m2"], groups: ["key-1", "VIPs", "crm:gone"] },
  });
  assert.deepEqual(savePayload(setMode(state, "audience")).audience, savePayload(state).audience);
});

test("switching to everyone saves no audience", () => {
  const state = setMode(loadEditor(flag({ audienceDetail: kept })), "everyone");
  assert.deepEqual(savePayload(state), { enabled: true, audience: null });
});

test("an off flag with nothing kept saves no audience", () => {
  assert.deepEqual(savePayload(loadEditor(flag({ enabled: false }))), { enabled: false, audience: null });
  assert.deepEqual(savePayload(setMode(loadEditor(flag()), "off")), { enabled: false, audience: null });
});

test("an off flag whose audience was emptied saves the empty audience", () => {
  const state = setMembers(removeGroup(removeGroup(removeGroup(loadEditor(flag({ enabled: false, audienceDetail: kept })), "key-1"), "VIPs"), "crm:gone"), []);
  assert.deepEqual(savePayload(state), { enabled: false, audience: { members: [], groups: [] } });
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
  assert.deepEqual(savePayload(state).audience?.groups, ["VIPs", "crm:gone", "key-2"]);
});

test("cannot save when the catalog refuses, when the editor can't manage flags, or when nothing changed", () => {
  const edit = (f: FeatureFlag) => setMode(loadEditor(f), "off");
  assert.equal(canSave(edit(flag()), true), true);
  assert.equal(canSave(edit(flag({ actions: { save: { allowed: false, reason: "It has a time window." } } })), true), false);
  assert.equal(canSave(edit(flag()), false), false);
  assert.equal(canSave(loadEditor(flag()), true), false);
});
