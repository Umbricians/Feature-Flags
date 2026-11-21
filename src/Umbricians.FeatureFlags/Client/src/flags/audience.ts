// What an audience holds and how the editor changes it. No backoffice imports, so Node can test it.
import type { AudienceCounts, AudienceGroup, AudienceGroupKind, FlagAudience, ProviderGroup, SaveAudience } from "../api/types.js";

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

/** What the server saves: every group's id, of every kind. */
export function toSaveAudience(audience: FlagAudience): SaveAudience {
  return { members: audience.members, groups: audience.groups.map((group) => group.id) };
}

/** How many of each, as the server counts them: named member groups are member groups, and unknown ids count as other groups. */
export function countAudience(audience: FlagAudience): AudienceCounts {
  const memberGroups = groupsOfKind(audience, "memberGroup", "namedMemberGroup").length;
  return { members: audience.members.length, memberGroups, groups: audience.groups.length - memberGroups };
}
