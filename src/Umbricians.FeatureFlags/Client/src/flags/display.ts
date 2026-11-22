// How flags are shown. No backoffice imports, so Node can test it.
import type { AudienceCounts, FeatureFlag, FlagActions, FlagOrder, FlagState } from "../api/types.js";

/** A flag as a collection item: its unique is its name. */
export interface FeatureFlagCollectionItem extends FeatureFlag {
  unique: string;
  entityType: string;
}

/** The flag list's "all sections" choice. It is a select's value, never sent to the server. */
export const ALL_SECTIONS = "*";

/** The select's value for a section filter: the section's name, "" for flags in no section, or all sections. */
export function sectionOption(section: string | null | undefined): string {
  return section === undefined || section === null ? ALL_SECTIONS : section;
}

/** The section filter for a select's value; null for every section. */
export function sectionFromOption(value: string): string | null {
  return value === ALL_SECTIONS ? null : value;
}

/** The name without its section in front, e.g. `NewFlow` for `Checkout.NewFlow` in the Checkout section. */
export function shortName(item: Pick<FeatureFlag, "name" | "section">): string {
  const prefix = item.section ? `${item.section}.` : "";
  return prefix && item.name.toLowerCase().startsWith(prefix.toLowerCase()) && item.name.length > prefix.length
    ? item.name.slice(prefix.length)
    : item.name;
}

export interface ListSection<T> {
  /** The section's name, or null for flags in no section. */
  name: string | null;
  items: Array<T>;
}

/**
 * Splits a page of flags into its sections, keeping their order. Sorted by last change, they stay in one section,
 * since splitting them would undo that order.
 */
export function sections<T extends Pick<FeatureFlag, "section">>(items: Array<T>, orderBy: FlagOrder | undefined): Array<ListSection<T>> {
  if (orderBy === "updated") return items.length ? [{ name: null, items }] : [];
  const result: Array<ListSection<T>> = [];
  for (const item of items) {
    const name = item.section ?? null;
    const last = result[result.length - 1];
    if (last && last.name === name) last.items.push(item);
    else result.push({ name, items: [item] });
  }
  return result;
}

export type FlagActionName = keyof FlagActions;

export interface Permitted {
  allowed: boolean;
  /** Why not, when the catalog refuses it; null when allowed, or when only the user's permission is missing. */
  reason: string | null;
  /** True when the catalog allows it but the user lacks the Change feature flags permission. */
  needsManage: boolean;
}

/** The catalog's answer, then the user's Change feature flags permission. */
export function mayDo(item: Pick<FeatureFlag, "actions">, action: FlagActionName, canManage: boolean): Permitted {
  const answer = item.actions[action];
  if (!answer.allowed) return { allowed: false, reason: answer.reason, needsManage: false };
  if (!canManage) return { allowed: false, reason: null, needsManage: true };
  return { allowed: true, reason: null, needsManage: false };
}

/** The list's quick action: a switch for a declared flag, Remove for an undeclared one. */
export function quickAction(
  item: Pick<FeatureFlag, "isDeclared" | "actions">,
  canManage: boolean,
): Permitted & { kind: "switch" | "remove" } {
  const kind = item.isDeclared ? "switch" : "remove";
  return { ...mayDo(item, kind, canManage), kind };
}

/**
 * What switching a flag on brings back, for the list's confirmation: its saved conditions, in one line, when it has any
 * (a flag that is off keeps them), otherwise null. An empty string means it has conditions but nothing to describe them.
 */
export function conditionsOnSwitchOn(item: Pick<FeatureFlag, "settings" | "summary">): string | null {
  return (item.settings?.conditions.length ?? 0) > 0 ? (item.summary ?? "") : null;
}

/**
 * What the list says under a flag's state: its conditions' summary when it is on with conditions (the audience alone
 * would leave out a time window), otherwise its audience in words, and for a flag that is off with conditions but no
 * audience, the summary of what it keeps. Null when there is nothing to say.
 */
export function listNote(item: Pick<FeatureFlag, "state" | "audience" | "summary" | "settings">): { kind: "audience" } | { kind: "summary"; text: string } | null {
  if (item.state === "conditions" && item.summary) return { kind: "summary", text: item.summary };
  if (item.audience) return { kind: "audience" };
  if (item.state === "off" && item.summary && (item.settings?.conditions.length ?? 0) > 0) return { kind: "summary", text: item.summary };
  return null;
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

/** The parts of what an audience leaves out that have anyone in them, for "except 1 member and 2 groups". */
export function excludedParts(counts: AudienceCounts | null): Array<{ kind: "members" | "groups"; count: number }> {
  if (!counts) return [];
  return [
    { kind: "members" as const, count: counts.excludedMembers },
    { kind: "groups" as const, count: counts.excludedGroups },
  ].filter((part) => part.count > 0);
}

/** Joins words as a list in English prose: "a", "a and b", "a, b and c". */
export function joinList(words: Array<string>, and: string): string {
  if (words.length <= 1) return words.join("");
  return `${words.slice(0, -1).join(", ")} ${and} ${words[words.length - 1]}`;
}
