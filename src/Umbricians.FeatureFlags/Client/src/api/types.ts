/** How a flag is evaluated. `custom` is something the backoffice can't edit, such as a time window. */
export type FlagState = "off" | "everyone" | "audience" | "custom";

export type FlagDeclaredIn = "configuration" | "flagSource" | "code";

export interface AudienceCounts {
  members: number;
  memberGroups: number;
  groups: number;
}

/** Who a flag is on for: member keys, member group keys and ids of groups from the site's group providers. */
export interface FlagAudience {
  members: Array<string>;
  memberGroups: Array<string>;
  groups: Array<string>;
}

export interface FeatureFlagListItem {
  name: string;
  description: string | null;
  group: string | null;
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
  editable: boolean;
  readOnlyReason: string | null;
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
  /** Each group and how many flags it has; a null name is flags in no group. */
  groups: Array<{ name: string | null; count: number }>;
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

export type FlagShow = "declared" | "changed" | "differs" | "twice" | "undeclared";
export type FlagOrder = "name" | "group" | "updated";

export interface FlagQuery {
  skip?: number;
  take?: number;
  filter?: string;
  state?: FlagState | "";
  show?: FlagShow;
  /** A group's name, or an empty string for flags in no group. */
  group?: string | null;
  tag?: string;
  orderBy?: FlagOrder;
}

/** Matches Umbraco's PagedViewModel. */
export interface PagedResult<T> {
  total: number;
  items: Array<T>;
}
