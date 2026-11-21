// How flags are shown. No backoffice imports, so Node can test it.
import type { AudienceCounts, FeatureFlagListItem, FlagAudience, FlagOrder, FlagState } from "../api/types.js";

/** A flag as a collection item: its unique is its name. */
export interface FeatureFlagCollectionItem extends FeatureFlagListItem {
  unique: string;
  entityType: string;
}

/** The name without its group in front, e.g. `NewFlow` for `Checkout.NewFlow` in the Checkout group. */
export function shortName(item: Pick<FeatureFlagListItem, "name" | "group">): string {
  const prefix = item.group ? `${item.group}.` : "";
  return prefix && item.name.toLowerCase().startsWith(prefix.toLowerCase()) && item.name.length > prefix.length
    ? item.name.slice(prefix.length)
    : item.name;
}

export interface FlagGroupSection<T> {
  /** The group's name, or null for flags in no group. */
  name: string | null;
  items: Array<T>;
}

/**
 * Splits a page of flags into sections by group, keeping their order. Sorted by last change, they stay in one section,
 * since grouping would undo that order.
 */
export function sections<T extends Pick<FeatureFlagListItem, "group">>(items: Array<T>, orderBy: FlagOrder | undefined): Array<FlagGroupSection<T>> {
  if (orderBy === "updated") return items.length ? [{ name: null, items }] : [];
  const result: Array<FlagGroupSection<T>> = [];
  for (const item of items) {
    const name = item.group ?? null;
    const last = result[result.length - 1];
    if (last && last.name === name) last.items.push(item);
    else result.push({ name, items: [item] });
  }
  return result;
}

/** What the quick switch does for a flag: switch it, nothing (read-only), or remove it (undeclared). */
export type SwitchKind = "switch" | "readOnly" | "remove";

export function switchKind(item: Pick<FeatureFlagListItem, "isDeclared" | "editable">, canManage: boolean): SwitchKind {
  if (!item.isDeclared) return canManage ? "remove" : "readOnly";
  return item.editable && canManage ? "switch" : "readOnly";
}

/** The tag colour for a state. */
export function stateColor(state: FlagState): "default" | "positive" | "warning" {
  return state === "everyone" ? "positive" : state === "custom" ? "warning" : "default";
}

/** The parts of an audience that have anyone in them, for "3 members and 1 member group". */
export function audienceParts(counts: AudienceCounts | null): Array<{ kind: "members" | "memberGroups" | "groups"; count: number }> {
  if (!counts) return [];
  return (["members", "memberGroups", "groups"] as const)
    .map((kind) => ({ kind, count: counts[kind] }))
    .filter((part) => part.count > 0);
}

export function countAudience(audience: FlagAudience): AudienceCounts {
  return { members: audience.members.length, memberGroups: audience.memberGroups.length, groups: audience.groups.length };
}

/** Joins words as a list in English prose: "a", "a and b", "a, b and c". */
export function joinList(words: Array<string>, and: string): string {
  if (words.length <= 1) return words.join("");
  return `${words.slice(0, -1).join(", ")} ${and} ${words[words.length - 1]}`;
}
