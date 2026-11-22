// What an audience holds and how the editor changes it. No backoffice imports, so Node can test it.
import { TARGETING_FILTER, type FlagCondition } from "../api/types.js";
import type { AudienceCounts, AudienceGroup, AudienceGroupKind, FlagAudience, ProviderGroup } from "../api/types.js";

export function emptyAudience(): FlagAudience {
  return { members: [], groups: [], defaultPercentage: 0, excludedMembers: [], excludedGroups: [] };
}

/** Which of the audience's two lists of members and groups an editor works on. */
export type AudienceSide = "included" | "excluded";

/**
 * One side of the audience as an audience of its own, so the functions below edit either side alike. Its members and
 * groups are that side's; the rest comes along unchanged.
 */
export function sideOf(audience: FlagAudience, side: AudienceSide): FlagAudience {
  return side === "included" ? audience : { ...audience, members: audience.excludedMembers, groups: audience.excludedGroups };
}

/** Puts an edited side back into the audience. */
export function withSide(audience: FlagAudience, side: AudienceSide, edited: FlagAudience): FlagAudience {
  return side === "included"
    ? { ...audience, members: edited.members, groups: edited.groups }
    : { ...audience, excludedMembers: edited.members, excludedGroups: edited.groups.map((group) => ({ ...group, percentage: 100 })) };
}

/** A percentage as the editor may set it: a whole number from 0 to 100; null when what was typed isn't a number. */
export function toPercentage(raw: unknown): number | null {
  const number = typeof raw === "number" ? raw : typeof raw === "string" && raw.trim() !== "" ? Number(raw) : NaN;
  return Number.isFinite(number) ? Math.min(100, Math.max(0, Math.round(number))) : null;
}

/** The percentage of everyone else, changed; unchanged when it isn't a number. */
export function withDefaultPercentage(audience: FlagAudience, raw: unknown): FlagAudience {
  const percentage = toPercentage(raw);
  return percentage === null ? audience : { ...audience, defaultPercentage: percentage };
}

/** One included group's percentage, changed; unchanged when it isn't a number. */
export function withGroupPercentage(audience: FlagAudience, id: string, raw: unknown): FlagAudience {
  const percentage = toPercentage(raw);
  return percentage === null
    ? audience
    : { ...audience, groups: audience.groups.map((group) => (group.id === id ? { ...group, percentage } : group)) };
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
    .map((key): AudienceGroup => ({ id: key, kind: "memberGroup", name: null, percentage: 100 }));
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
  return { ...audience, groups: [...audience.groups, { id: group.id, kind: "providerGroup", name: group.name, percentage: 100 }] };
}

/**
 * The audience as a Microsoft.Targeting condition, in the parameters the server writes. Every group's id goes in, of
 * every kind, with its percentage. `Exclusion` is there only when something is left out.
 */
export function toTargetingCondition(audience: FlagAudience): FlagCondition {
  const targeting: Record<string, unknown> = {
    Users: audience.members,
    Groups: audience.groups.map((group) => ({ Name: group.id, RolloutPercentage: group.percentage })),
    DefaultRolloutPercentage: audience.defaultPercentage,
  };
  if (audience.excludedMembers.length > 0 || audience.excludedGroups.length > 0) {
    targeting.Exclusion = { Users: audience.excludedMembers, Groups: audience.excludedGroups.map((group) => group.id) };
  }
  return { name: TARGETING_FILTER, parameters: { Audience: targeting } };
}

/** Whether a condition is the audience's: Microsoft matches `Targeting` and `Microsoft.Targeting` as the same filter. */
export function isTargeting(condition: FlagCondition): boolean {
  return condition.name.toLowerCase() === TARGETING_FILTER.toLowerCase() || condition.name.toLowerCase() === "targeting";
}

/** How many of each, as the server counts them: named member groups are member groups, and unknown ids count as other groups. */
export function countAudience(audience: FlagAudience): AudienceCounts {
  const memberGroups = groupsOfKind(audience, "memberGroup", "namedMemberGroup").length;
  return {
    members: audience.members.length,
    memberGroups,
    groups: audience.groups.length - memberGroups,
    partialGroups: audience.groups.filter((group) => group.percentage < 100).length,
    defaultPercentage: audience.defaultPercentage,
    excludedMembers: audience.excludedMembers.length,
    excludedGroups: audience.excludedGroups.length,
  };
}

/**
 * The audience a Microsoft.Targeting condition's parameters hold: members, groups with their percentages, the percentage
 * of everyone else, and what is left out. Each group is what the site knows it to be: a group the site hasn't described
 * is a member group when its id is a key, and otherwise unknown. Numbers may be written as text, and a missing group
 * percentage is 0, as Microsoft reads it.
 */
export function audienceFromParameters(parameters: Record<string, unknown>, known: ReadonlyArray<AudienceGroup> = []): FlagAudience {
  const audience = child(parameters, "Audience");
  const exclusion = child(audience, "Exclusion");
  const describe = (id: string, percentage: number): AudienceGroup => {
    const found = known.find((group) => group.id === id);
    return { id, kind: found?.kind ?? (GUID.test(id) ? "memberGroup" : "unknown"), name: found?.name ?? null, percentage };
  };
  const texts = (list: unknown) => (Array.isArray(list) ? list.map(textOf).filter((id): id is string => id !== null) : []);
  const groups = child(audience, "Groups");
  return {
    members: texts(child(audience, "Users")),
    groups: Array.isArray(groups)
      ? groups.flatMap((group) => {
          const id = textOf(child(group, "Name"));
          return id === null ? [] : [describe(id, toPercentage(child(group, "RolloutPercentage")) ?? 0)];
        })
      : [],
    defaultPercentage: toPercentage(child(audience, "DefaultRolloutPercentage")) ?? 0,
    excludedMembers: texts(child(exclusion, "Users")),
    excludedGroups: texts(child(exclusion, "Groups")).map((id) => describe(id, 100)),
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
