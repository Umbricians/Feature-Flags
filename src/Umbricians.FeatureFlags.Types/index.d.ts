// Types for extending the Umbricians Feature Flags backoffice. Types only: this package has no runtime code and no
// import-map entry, so a `declare const` below has no value at runtime. Write the string (or build a token from it);
// the literal types make a typo fail to compile. The strings are promised for the life of an Umbraco major.
import type { ManifestElement } from "@umbraco-cms/backoffice/extension-api";
import type { UmbConditionConfigBase } from "@umbraco-cms/backoffice/extension-api";
import type { Observable } from "@umbraco-cms/backoffice/external/rxjs";

// ---- Entity types, aliases and strings -------------------------------------------------------------------------

/** The Feature Flags item in the Settings tree, which opens the flag list. */
export declare const FEATURE_FLAGS_ROOT_ENTITY_TYPE: "umbricians-feature-flags-root";
/** One flag. Its `unique` is the flag's name. Register entity actions for this type. */
export declare const FEATURE_FLAG_ENTITY_TYPE: "umbricians-feature-flags-flag";

export declare const FEATURE_FLAGS_ROOT_WORKSPACE_ALIAS: "Umbricians.FeatureFlags.Workspace.Root";
export declare const FEATURE_FLAG_WORKSPACE_ALIAS: "Umbricians.FeatureFlags.Workspace.Flag";
export declare const FEATURE_FLAGS_COLLECTION_ALIAS: "Umbricians.FeatureFlags.Collection";
export declare const FEATURE_FLAGS_MENU_ITEM_ALIAS: "Umbricians.FeatureFlags.MenuItem.Root";

/** The `type` of a manifest that registers a condition's editor. */
export declare const FEATURE_FLAGS_CONDITION_MANIFEST_TYPE: "umbriciansFeatureFlagsCondition";

/** The alias of the read-only flag workspace context: `new UmbContextToken<UmbriciansFeatureFlagWorkspaceContext>(alias)`. */
export declare const FEATURE_FLAG_WORKSPACE_CONTEXT_ALIAS: "Umbricians.FeatureFlags.FlagWorkspaceContext.Public";

/** The alias of the condition that permits an extension to editors who may view or change flags. */
export declare const FEATURE_FLAGS_PERMISSION_CONDITION_ALIAS: "Umbricians.FeatureFlags.Condition.Permission";
export declare const FEATURE_FLAGS_PERMISSION_VIEW: "Umbricians.FeatureFlags.View";
export declare const FEATURE_FLAGS_PERMISSION_MANAGE: "Umbricians.FeatureFlags.Manage";

// ---- The permission condition ----------------------------------------------------------------------------------

/** `conditions: [{ alias: FEATURE_FLAGS_PERMISSION_CONDITION_ALIAS, permission: FEATURE_FLAGS_PERMISSION_VIEW }]` */
export interface FeatureFlagsPermissionConditionConfig
  extends UmbConditionConfigBase<typeof FEATURE_FLAGS_PERMISSION_CONDITION_ALIAS> {
  permission: typeof FEATURE_FLAGS_PERMISSION_VIEW | typeof FEATURE_FLAGS_PERMISSION_MANAGE;
}

// ---- A condition's editor --------------------------------------------------------------------------------------

/**
 * The element of a condition's editor. It is given the condition's parameters as a JSON object in `value`, and
 * `readonly` when the editor may not change the flag. When the editor changes the parameters it sets `value` to a new
 * JSON object and dispatches a `change` event (Umbraco's `UmbChangeEvent`, which bubbles). The `value` it is given is
 * frozen, so never change it in place.
 */
export interface UmbriciansFeatureFlagsConditionElement extends HTMLElement {
  value: Record<string, unknown>;
  readonly?: boolean;
}

export interface MetaUmbriciansFeatureFlagsCondition {
  /** The `Name` of the condition's C# class, such as `Contoso.Culture`. Matched ignoring case. */
  name: string;
}

/** The editor for one of a flag's conditions, found by `meta.name`, the name of the C# `IFeatureFlagCondition`. */
export interface ManifestUmbriciansFeatureFlagsCondition extends ManifestElement<UmbriciansFeatureFlagsConditionElement> {
  type: typeof FEATURE_FLAGS_CONDITION_MANIFEST_TYPE;
  meta: MetaUmbriciansFeatureFlagsCondition;
}

declare global {
  interface UmbExtensionManifestMap {
    umbriciansFeatureFlagsCondition: ManifestUmbriciansFeatureFlagsCondition;
  }
  interface UmbExtensionConditionConfigMap {
    umbriciansFeatureFlagsPermission: FeatureFlagsPermissionConditionConfig;
  }
}

// ---- The flag workspace's read-only context --------------------------------------------------------------------

/**
 * Reach it from an element inside a flag's workspace (a workspace view, for instance) with
 * `this.consumeContext(new UmbContextToken<UmbriciansFeatureFlagWorkspaceContext>(FEATURE_FLAG_WORKSPACE_CONTEXT_ALIAS), ...)`.
 * It is read-only: changes go through conditions or the Management API.
 */
export interface UmbriciansFeatureFlagWorkspaceContext {
  /** The flag being edited, as saved: undefined until it has loaded. */
  readonly flag: Observable<FeatureFlag | undefined>;
}

// ---- The API's shapes ------------------------------------------------------------------------------------------

/** How a flag is evaluated. `conditions` is on with conditions registered by the package or a developer; `custom` is something the backoffice can't edit. */
export type FeatureFlagState = "off" | "everyone" | "audience" | "conditions" | "custom";

export type FlagDeclaredIn = "configuration" | "flagSource" | "code";

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

export interface FlagAction {
  allowed: boolean;
  reason: string | null;
}

/** What an editor may do with a flag; each is refused with a reason when not allowed. */
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
  updatedByName: string | null;
  updatedUtc: string;
}

export interface AudienceCounts {
  members: number;
  memberGroups: number;
  groups: number;
  partialGroups?: number;
  defaultPercentage: number;
  excludedMembers: number;
  excludedGroups: number;
}

export type AudienceGroupKind = "memberGroup" | "namedMemberGroup" | "providerGroup" | "unknown";

export interface AudienceGroup {
  id: string;
  kind: AudienceGroupKind;
  name: string | null;
  percentage: number;
}

export interface FlagAudience {
  members: Array<string>;
  groups: Array<AudienceGroup>;
  defaultPercentage: number;
  excludedMembers: Array<string>;
  excludedGroups: Array<AudienceGroup>;
}

/** A flag, as the Management API returns it. */
export interface FeatureFlag {
  name: string;
  isDeclared: boolean;
  declaredIn: FlagDeclaredIn;
  declaredTwice: boolean;
  section: string | null;
  description: string | null;
  tags: Array<string>;
  isPublic: boolean;
  state: FeatureFlagState;
  defaultState: FeatureFlagState;
  /** What applies now; null when it uses more than settings can say (state `custom`). */
  settings: FlagSettings | null;
  defaultSettings: FlagSettings | null;
  override: FlagOverride | null;
  differsFromDefault: boolean;
  actions: FlagActions;
  /** What its conditions amount to in one line. */
  summary: string | null;
  enabled: boolean;
  audience: AudienceCounts | null;
  audienceDetail: FlagAudience | null;
}
