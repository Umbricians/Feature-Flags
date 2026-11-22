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
  sideOf,
  toPercentage,
  toTargetingCondition,
  withDefaultPercentage,
  withGroupPercentage,
  withMemberGroupKeys,
  withSide,
  withoutGroup,
  withProviderGroup,
} from "./audience.ts";

const crm: ProviderGroup = { id: "crm:premium", name: "Premium in the CRM", description: null };

const audience: FlagAudience = {
  ...emptyAudience(),
  members: ["m1"],
  groups: [
    { id: "key-1", kind: "memberGroup", name: "Premium members", percentage: 100 },
    { id: "VIPs", kind: "namedMemberGroup", name: "VIPs", percentage: 100 },
    { id: "crm:premium", kind: "providerGroup", name: "Premium in the CRM", percentage: 100 },
    { id: "crm:gone", kind: "unknown", name: null, percentage: 100 },
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
  assert.deepEqual(picked.groups[3], { id: "key-2", kind: "memberGroup", name: null, percentage: 100 });
  assert.deepEqual(picked.members, ["m1"]);
  const kept = withMemberGroupKeys(audience, ["key-1", "key-2"]);
  assert.equal(kept.groups[0].name, "Premium members");
  assert.equal(kept.groups.length, 5);
});

test("ticking a provider group adds it once, unticking removes it", () => {
  const ticked = withProviderGroup(emptyAudience(), crm, true);
  assert.deepEqual(ticked.groups, [{ id: "crm:premium", kind: "providerGroup", name: "Premium in the CRM", percentage: 100 }]);
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
  assert.deepEqual(countAudience(audience), { members: 1, memberGroups: 2, groups: 2, defaultPercentage: 0, excludedMembers: 0, excludedGroups: 0 });
  assert.deepEqual(countAudience(emptyAudience()), { members: 0, memberGroups: 0, groups: 0, defaultPercentage: 0, excludedMembers: 0, excludedGroups: 0 });
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

const withEverything: FlagAudience = {
  members: ["m1"],
  groups: [
    { id: "key-1", kind: "memberGroup", name: "Premium members", percentage: 40 },
    { id: "crm:premium", kind: "providerGroup", name: "Premium in the CRM", percentage: 100 },
  ],
  defaultPercentage: 25,
  excludedMembers: ["x1"],
  excludedGroups: [{ id: "VIPs", kind: "namedMemberGroup", name: "VIPs", percentage: 100 }],
};

test("percentages and exclusions are written in Microsoft's shape", () => {
  assert.deepEqual(toTargetingCondition(withEverything).parameters, {
    Audience: {
      Users: ["m1"],
      Groups: [
        { Name: "key-1", RolloutPercentage: 40 },
        { Name: "crm:premium", RolloutPercentage: 100 },
      ],
      DefaultRolloutPercentage: 25,
      Exclusion: { Users: ["x1"], Groups: ["VIPs"] },
    },
  });
});

test("an audience with percentages and exclusions round trips, so saving never drops them", () => {
  const read = audienceFromParameters(toTargetingCondition(withEverything).parameters, [
    ...withEverything.groups,
    ...withEverything.excludedGroups,
  ]);
  assert.deepEqual(read, withEverything);
});

test("nothing excluded writes no Exclusion", () => {
  assert.equal("Exclusion" in (toTargetingCondition(audience).parameters.Audience as object), false);
});

test("percentages written as text are read, and a group without one is 0 as Microsoft reads it", () => {
  const read = audienceFromParameters({
    Audience: { DefaultRolloutPercentage: "50", Groups: [{ Name: "a", RolloutPercentage: "30" }, { Name: "b" }], Exclusion: { Users: [7], Groups: ["c"] } },
  });
  assert.equal(read.defaultPercentage, 50);
  assert.deepEqual(read.groups.map((group) => group.percentage), [30, 0]);
  assert.deepEqual(read.excludedMembers, ["7"]);
  assert.deepEqual(read.excludedGroups.map((group) => [group.id, group.kind]), [["c", "unknown"]]);
});

test("a typed percentage is rounded and kept within 0 to 100, and anything else is not a percentage", () => {
  assert.equal(toPercentage("42.6"), 43);
  assert.equal(toPercentage(150), 100);
  assert.equal(toPercentage("-5"), 0);
  assert.equal(toPercentage(""), null);
  assert.equal(toPercentage("lots"), null);
  assert.equal(toPercentage(undefined), null);
});

test("changing the default or a group's percentage touches only that, and a non-number changes nothing", () => {
  assert.equal(withDefaultPercentage(withEverything, "60").defaultPercentage, 60);
  assert.equal(withDefaultPercentage(withEverything, "x"), withEverything);
  const changed = withGroupPercentage(withEverything, "crm:premium", "10");
  assert.deepEqual(changed.groups.map((group) => group.percentage), [40, 10]);
  assert.equal(withGroupPercentage(withEverything, "crm:premium", ""), withEverything);
});

test("the excluded side is edited with the same functions and keeps every group's percentage at 100", () => {
  const excluded = sideOf(withEverything, "excluded");
  assert.deepEqual(excluded.members, ["x1"]);
  const edited = withSide(withEverything, "excluded", withProviderGroup(excluded, crm, true));
  assert.deepEqual(edited.excludedGroups.map((group) => [group.id, group.percentage]), [["VIPs", 100], ["crm:premium", 100]]);
  assert.deepEqual(edited.groups, withEverything.groups);
  assert.equal(edited.defaultPercentage, 25);
  const included = withSide(withEverything, "included", { ...sideOf(withEverything, "included"), members: ["m9"] });
  assert.deepEqual(included.members, ["m9"]);
  assert.deepEqual(included.excludedMembers, ["x1"]);
});

test("counts include the percentage and what is left out", () => {
  assert.deepEqual(countAudience(withEverything), { members: 1, memberGroups: 1, groups: 1, defaultPercentage: 25, excludedMembers: 1, excludedGroups: 1 });
});
