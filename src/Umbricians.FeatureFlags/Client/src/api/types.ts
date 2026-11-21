/** How a flag is evaluated. `custom` is something the backoffice can't edit, such as a time window. */
export type FlagState = "off" | "everyone" | "audience" | "custom";

export type FlagDeclaredIn = "configuration" | "flagSource" | "code";

export interface AudienceCounts {
  members: number;
  memberGroups: number;
  groups: number;
}

/** What a group id in an audience is. `unknown` is a deleted member group's key or a provider group no longer offered. */
export type AudienceGroupKind = "memberGroup" | "namedMemberGroup" | "providerGroup" | "unknown";

/** A group in an audience: the id the flag stores, what the site knows it to be, and its name when it knows one. */
export interface AudienceGroup {
  id: string;
  kind: AudienceGroupKind;
  name: string | null;
}

/** Who a flag is on for: member keys and groups of every kind. */
export interface FlagAudience {
  members: Array<string>;
  groups: Array<AudienceGroup>;
}

/** An audience as it is saved: member keys and every group's id. */
export interface SaveAudience {
  members: Array<string>;
  groups: Array<string>;
}

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

export interface FeatureFlagListItem {
  name: string;
  description: string | null;
  section: string | null;
  tags: Array<string>;
  declaredIn: FlagDeclaredIn;
  declaredTwice: boolean;
  /** False for a saved setting whose flag nobody declares any more. */
  isDeclared: boolean;
  state: FlagState;
  /** Whether it is switched on; a flag that is off may keep an audience for later. */
  enabled: boolean;
  audience: AudienceCounts | null;
  hasOverride: boolean;
  differsFromDefault: boolean;
  /** What an editor may do with it, as the catalog decides; each is refused with a reason when not allowed. */
  actions: FlagActions;
  updatedBy: { key: string; name: string | null } | null;
  updatedUtc: string | null;
}

export interface FeatureFlag {
  flag: FeatureFlagListItem;
  defaultState: FlagState;
  audienceDetail: FlagAudience | null;
  /** The saved setting in Microsoft's feature_management JSON, when there is one. */
  overrideDefinition: string | null;
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
