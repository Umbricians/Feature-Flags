// Plain strings only: manifests import these, and the manifest tests run in Node without the backoffice.

/** The Feature Flags item in the Settings tree, which opens the flag list. */
export const FEATURE_FLAGS_ROOT_ENTITY_TYPE = "umbricians-feature-flags-root";

/** One flag, edited in its own workspace. Its unique is the flag's name. */
export const FEATURE_FLAG_ENTITY_TYPE = "umbricians-feature-flags-flag";

/** The alias of the Feature Flags item in the Settings menu. */
export const FEATURE_FLAGS_MENU_ITEM_ALIAS = "Umbricians.FeatureFlags.MenuItem.Root";

export const FEATURE_FLAGS_ROOT_WORKSPACE_ALIAS = "Umbricians.FeatureFlags.Workspace.Root";
export const FEATURE_FLAG_WORKSPACE_ALIAS = "Umbricians.FeatureFlags.Workspace.Flag";
export const FEATURE_FLAGS_COLLECTION_ALIAS = "Umbricians.FeatureFlags.Collection";
export const FEATURE_FLAGS_COLLECTION_REPOSITORY_ALIAS = "Umbricians.FeatureFlags.Repository.Collection";

/** The `type` of a manifest that registers the editor of a condition. */
export const FEATURE_FLAGS_CONDITION_MANIFEST_TYPE = "umbriciansFeatureFlagsCondition";

/**
 * The alias a developer builds a context token from, `new UmbContextToken("<alias>")`, to read the flag being
 * edited. Umbraco resolves context tokens by this string.
 */
export const FEATURE_FLAG_WORKSPACE_CONTEXT_ALIAS = "Umbricians.FeatureFlags.FlagWorkspaceContext.Public";

export { FEATURE_FLAGS_PERMISSION_CONDITION_ALIAS } from "./permissions/constants.js";

/** Umbraco's own condition aliases, written out so manifests don't import the backoffice. */
export const UMB_WORKSPACE_CONDITION = "Umb.Condition.WorkspaceAlias";
export const UMB_COLLECTION_CONDITION = "Umb.Condition.CollectionAlias";

/** Flags per page in the flag list. */
export const FEATURE_FLAGS_PAGE_SIZE = 25;
