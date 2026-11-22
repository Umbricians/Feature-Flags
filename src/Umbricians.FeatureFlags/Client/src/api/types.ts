/** How a flag is evaluated. `conditions` is on with conditions registered by the package or a developer; `custom` is something the backoffice can't edit, such as a filter nobody registered. */
export type FlagState = "off" | "everyone" | "audience" | "conditions" | "custom";

export type FlagDeclaredIn = "configuration" | "flagSource" | "code";

export interface AudienceCounts {
  members: number;
  memberGroups: number;
  groups: number;
  /** How many of the groups are included in part only (below 100%, including 0%). Worked out by the editor; the server doesn't send it. */
  partialGroups?: number;
  /** The percentage of everyone else it includes, 0 to 100. */
  defaultPercentage: number;
  excludedMembers: number;
  excludedGroups: number;
}

/** What a group id in an audience is. `unknown` is a deleted member group's key or a provider group no longer offered. */
export type AudienceGroupKind = "memberGroup" | "namedMemberGroup" | "providerGroup" | "unknown";

/** A group in an audience: the id the flag stores, what the site knows it to be, and its name when it knows one. */
export interface AudienceGroup {
  id: string;
  kind: AudienceGroupKind;
  name: string | null;
  /** The percentage of the group that is included, 0 to 100. Always 100 for a group that is left out. */
  percentage: number;
}

/**
 * Who a flag is on for: member keys and groups of every kind, a percentage of everyone else, and the members and groups
 * left out. Percentages are of signed-in members only.
 */
export interface FlagAudience {
  members: Array<string>;
  groups: Array<AudienceGroup>;
  /** The percentage, 0 to 100, of every member not otherwise included. */
  defaultPercentage: number;
  excludedMembers: Array<string>;
  excludedGroups: Array<AudienceGroup>;
}

/** Whether a flag with several conditions needs any one of them to hold, or all of them. */
export type ConditionMatch = "any" | "all";

/** One of Microsoft's filters on a flag: its name, such as `Microsoft.Targeting`, and its parameters as JSON. */
export interface FlagCondition {
  name: string;
  parameters: Record<string, unknown>;
}

/** A flag's settings: whether it is on, and the conditions that say who or when it is on for. */
export interface FlagSettings {
  enabled: boolean;
  conditions: Array<FlagCondition>;
  match: ConditionMatch;
}

/** What a save sends. */
export type SaveSettings = FlagSettings;

/** The name of the package's audience condition, Microsoft's targeting filter. */
export const TARGETING_FILTER = "Microsoft.Targeting";

export interface FlagAction {
  allowed: boolean;
  reason: string | null;
}

export interface FlagActions {
  switch: FlagAction;
  save: FlagAction;
  reset: FlagAction;
  remove: FlagAction;
}

/** A flag's override as stored. */
export interface FlagOverride {
  /** The flag in Microsoft's feature_management JSON. */
  definition: string;
  updatedBy: string | null;
  /** The name of the user who saved it, for display. */
  updatedByName: string | null;
  updatedUtc: string;
}

/** A flag: every field of the C# `FeatureFlag`, then what only the backoffice shows. */
export interface FeatureFlag {
  name: string;
  /** False for a saved setting whose flag nobody declares any more. */
  isDeclared: boolean;
  declaredIn: FlagDeclaredIn;
  declaredTwice: boolean;
  section: string | null;
  description: string | null;
  tags: Array<string>;
  isPublic: boolean;
  state: FlagState;
  defaultState: FlagState;
  /** What applies now; null when it uses more than settings can say (state `custom`). */
  settings: FlagSettings | null;
  defaultSettings: FlagSettings | null;
  override: FlagOverride | null;
  differsFromDefault: boolean;
  /** What an editor may do with it, as the catalog decides; each is refused with a reason when not allowed. */
  actions: FlagActions;
  /** What its conditions amount to in one line. */
  summary: string | null;

  /** Whether it is switched on; a flag that is off may keep conditions for later. */
  enabled: boolean;
  /** How many members and groups its audience has, when it has one. */
  audience: AudienceCounts | null;
  /** Who it is on for, with what each group is, when its only condition is an audience the backoffice can read. */
  audienceDetail: FlagAudience | null;
}

export interface FeatureFlagSummary {
  /** Each section and how many flags it has; a null name is flags in no section. */
  sections: Array<{ name: string | null; count: number }>;
  tags: Array<string>;
  total: number;
  undeclared: number;
  /** Whether settings from the backoffice are switched off on this site. */
  readOnly: boolean;
  /** The site's environment, e.g. Production, so editors know where they are changing flags. */
  environment: string;
}

/** A site: a top-level content node the Root condition can name. A deleted one has no name. */
export interface SiteRoot {
  key: string;
  name?: string | null;
  domains: Array<string>;
  deleted: boolean;
}

/** A condition registered on the site: its filter name, and what editors call it. */
export interface RegisteredCondition {
  name: string;
  displayName: string;
}

export interface ProviderGroup {
  id: string;
  name: string;
  description: string | null;
}

/** Which list: declared flags, or settings left from flags nobody declares any more. */
export type FlagShow = "declared" | "undeclared";
/** Notes a flag must have, every one of them, to be listed. */
export type FlagMark = "changed" | "differs" | "twice";
export type FlagOrder = "name" | "section" | "updated";

export interface FlagQuery {
  skip?: number;
  take?: number;
  filter?: string;
  state?: FlagState | "";
  show?: FlagShow;
  marks?: Array<FlagMark>;
  /** A section's name, or an empty string for flags in no section. */
  section?: string | null;
  tag?: string;
  orderBy?: FlagOrder;
}

/** Matches Umbraco's PagedViewModel. */
export interface PagedResult<T> {
  total: number;
  items: Array<T>;
}
