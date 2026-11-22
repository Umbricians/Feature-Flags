import { test } from "node:test";
import assert from "node:assert/strict";
import type { FlagAudience, ProviderGroup } from "../api/types.ts";
import {
  audienceFromParameters,
  countAudience,
  emptyAudience,
  groupsOfKind,
  hasGroup,
  memberGroupKeys,
  toTargetingCondition,
  withMemberGroupKeys,
  withoutGroup,
  withProviderGroup,
} from "./audience.ts";

const crm: ProviderGroup = { id: "crm:premium", name: "Premium in the CRM", description: null };

const audience: FlagAudience = {
  members: ["m1"],
  groups: [
    { id: "key-1", kind: "memberGroup", name: "Premium members" },
    { id: "VIPs", kind: "namedMemberGroup", name: "VIPs" },
    { id: "crm:premium", kind: "providerGroup", name: "Premium in the CRM" },
    { id: "crm:gone", kind: "unknown", name: null },
  ],
};

test("the member group picker gets only member group keys", () => {
  assert.deepEqual(memberGroupKeys(audience), ["key-1"]);
  assert.deepEqual(memberGroupKeys(emptyAudience()), []);
});

test("picking member groups keeps named member groups, provider groups and unknown ids", () => {
  const picked = withMemberGroupKeys(audience, ["key-2"]);
  assert.deepEqual(
    picked.groups.map((group) => group.id),
    ["VIPs", "crm:premium", "crm:gone", "key-2"],
  );
  assert.deepEqual(picked.groups[3], { id: "key-2", kind: "memberGroup", name: null });
  assert.deepEqual(picked.members, ["m1"]);
  const kept = withMemberGroupKeys(audience, ["key-1", "key-2"]);
  assert.equal(kept.groups[0].name, "Premium members");
  assert.equal(kept.groups.length, 5);
});

test("ticking a provider group adds it once, unticking removes it", () => {
  const ticked = withProviderGroup(emptyAudience(), crm, true);
  assert.deepEqual(ticked.groups, [{ id: "crm:premium", kind: "providerGroup", name: "Premium in the CRM" }]);
  assert.equal(withProviderGroup(ticked, crm, true), ticked);
  assert.equal(hasGroup(ticked, "crm:premium"), true);
  assert.equal(hasGroup(withProviderGroup(ticked, crm, false), "crm:premium"), false);
});

test("a named member group or an unknown id is removed by its id", () => {
  assert.deepEqual(
    withoutGroup(audience, "crm:gone").groups.map((group) => group.id),
    ["key-1", "VIPs", "crm:premium"],
  );
  assert.deepEqual(groupsOfKind(audience, "namedMemberGroup", "unknown").map((group) => group.id), ["VIPs", "crm:gone"]);
});

test("a saved audience sends every group's id", () => {
  assert.deepEqual(toTargetingCondition(audience), {
    name: "Microsoft.Targeting",
    parameters: {
      Audience: {
        Users: ["m1"],
        Groups: ["key-1", "VIPs", "crm:premium", "crm:gone"].map((Name) => ({ Name, RolloutPercentage: 100 })),
        DefaultRolloutPercentage: 0,
      },
    },
  });
});

test("counts named member groups as member groups and unknown ids as other groups", () => {
  assert.deepEqual(countAudience(audience), { members: 1, memberGroups: 2, groups: 2 });
  assert.deepEqual(countAudience(emptyAudience()), { members: 0, memberGroups: 0, groups: 0 });
});

test("an audience round trips through targeting parameters, keeping what the site knows about each group", () => {
  const known = audience.groups;
  const read = audienceFromParameters(toTargetingCondition(audience).parameters, known);
  assert.deepEqual(read, audience);
});

test("groups the site hasn't described are member groups when they are keys, and unknown otherwise", () => {
  const read = audienceFromParameters(
    { audience: { users: [1001, "m2"], groups: [{ name: "7f3a9c21-4e8b-4d6f-a1c5-0b2e9d4f8a67" }, { Name: "mystery" }, {}] } },
  );
  assert.deepEqual(read.members, ["1001", "m2"]);
  assert.deepEqual(read.groups.map((g) => g.kind), ["memberGroup", "unknown"]);
});

test("empty parameters are an empty audience", () => {
  assert.deepEqual(audienceFromParameters({}), emptyAudience());
});
