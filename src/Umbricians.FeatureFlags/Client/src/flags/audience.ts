// What an audience holds and how the editor changes it. No backoffice imports, so Node can test it.
import { TARGETING_FILTER, type FlagCondition } from "../api/types.js";
import type { AudienceCounts, AudienceGroup, AudienceGroupKind, FlagAudience, ProviderGroup } from "../api/types.js";

export function emptyAudience(): FlagAudience {
  return { members: [], groups: [] };
}

/** The groups of the given kinds, in the order the audience has them. */
export function groupsOfKind(audience: FlagAudience, ...kinds: Array<AudienceGroupKind>): Array<AudienceGroup> {
  return audience.groups.filter((group) => kinds.includes(group.kind));
}

/** The member groups the picker holds: only those the site knows by key. */
export function memberGroupKeys(audience: FlagAudience): Array<string> {
  return groupsOfKind(audience, "memberGroup").map((group) => group.id);
}

/**
 * Replaces the member groups chosen by key with those the picker now holds, keeping the names of the ones still there.
 * Every other group stays as it was, in its place, so groups the picker can't show survive a change to it.
 */
export function withMemberGroupKeys(audience: FlagAudience, keys: Array<string>): FlagAudience {
  const kept = audience.groups.filter((group) => group.kind !== "memberGroup" || keys.includes(group.id));
  const added = keys
    .filter((key) => !kept.some((group) => group.id === key))
    .map((key): AudienceGroup => ({ id: key, kind: "memberGroup", name: null }));
  return { ...audience, groups: [...kept, ...added] };
}

export function hasGroup(audience: FlagAudience, id: string): boolean {
  return audience.groups.some((group) => group.id === id);
}

export function withoutGroup(audience: FlagAudience, id: string): FlagAudience {
  return { ...audience, groups: audience.groups.filter((group) => group.id !== id) };
}

/** Adds a provider's group once, or takes it out. */
export function withProviderGroup(audience: FlagAudience, group: ProviderGroup, checked: boolean): FlagAudience {
  if (!checked) return withoutGroup(audience, group.id);
  if (hasGroup(audience, group.id)) return audience;
  return { ...audience, groups: [...audience.groups, { id: group.id, kind: "providerGroup", name: group.name }] };
}

/**
 * The audience as a Microsoft.Targeting condition, in the parameters the server writes: everyone in it, and nobody
 * else. Every group's id goes in, of every kind.
 */
export function toTargetingCondition(audience: FlagAudience): FlagCondition {
  return {
    name: TARGETING_FILTER,
    parameters: {
      Audience: {
        Users: audience.members,
        Groups: audience.groups.map((group) => ({ Name: group.id, RolloutPercentage: 100 })),
        DefaultRolloutPercentage: 0,
      },
    },
  };
}

/** Whether a condition is the audience's: Microsoft matches `Targeting` and `Microsoft.Targeting` as the same filter. */
export function isTargeting(condition: FlagCondition): boolean {
  return condition.name.toLowerCase() === TARGETING_FILTER.toLowerCase() || condition.name.toLowerCase() === "targeting";
}

/** How many of each, as the server counts them: named member groups are member groups, and unknown ids count as other groups. */
export function countAudience(audience: FlagAudience): AudienceCounts {
  const memberGroups = groupsOfKind(audience, "memberGroup", "namedMemberGroup").length;
  return { members: audience.members.length, memberGroups, groups: audience.groups.length - memberGroups };
}

/**
 * The audience a Microsoft.Targeting condition's parameters hold: its members and the names of its groups, each group
 * with what the site knows it to be. A group the site hasn't described is a member group when its id is a key, and
 * otherwise unknown. Percentages and exclusions are not read.
 */
export function audienceFromParameters(parameters: Record<string, unknown>, known: ReadonlyArray<AudienceGroup> = []): FlagAudience {
  const audience = child(parameters, "Audience");
  const users = child(audience, "Users");
  const groups = child(audience, "Groups");
  const members = Array.isArray(users) ? users.map(textOf).filter((id): id is string => id !== null) : [];
  const ids = Array.isArray(groups) ? groups.map((group) => textOf(child(group, "Name"))).filter((id): id is string => id !== null) : [];
  return {
    members,
    groups: ids.map(
      (id): AudienceGroup => known.find((group) => group.id === id) ?? { id, kind: GUID.test(id) ? "memberGroup" : "unknown", name: null },
    ),
  };
}

const GUID = /^[0-9a-f]{8}-?([0-9a-f]{4}-?){3}[0-9a-f]{12}$/i;

// Microsoft reads configuration keys without regard to case, so this does too.
function child(parent: unknown, name: string): unknown {
  if (typeof parent !== "object" || parent === null || Array.isArray(parent)) return undefined;
  const key = Object.keys(parent).find((candidate) => candidate.toLowerCase() === name.toLowerCase());
  return key === undefined ? undefined : (parent as Record<string, unknown>)[key];
}

function textOf(value: unknown): string | null {
  return typeof value === "string" || typeof value === "number" || typeof value === "boolean" ? String(value) : null;
}
