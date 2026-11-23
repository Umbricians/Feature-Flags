// Compile-time checks, run by `tsc --noEmit`, written the way a package that extends Feature Flags writes them: if a
// rename in Umbraco's types breaks the declarations in index.d.ts, one of these stops compiling. The declarations
// themselves are checked by test/check-declarations.mjs, since tsc skips .d.ts files with skipLibCheck.
import type { FeatureFlagEntityType, FeatureFlagsPermissionConditionConfig } from "../index.d.ts";

const editor: UmbExtensionManifest = {
  type: "umbriciansFeatureFlagsCondition",
  alias: "Contoso.FeatureFlags.Condition.Culture",
  name: "Culture condition editor",
  element: () => import("./consumer.element.ts"),
  meta: { name: "Contoso.Culture" },
};

// @ts-expect-error meta.name, the C# condition's name, is required
const noName: UmbExtensionManifest = {
  type: "umbriciansFeatureFlagsCondition",
  alias: "Contoso.FeatureFlags.Condition.Culture",
  name: "Culture condition editor",
  meta: {},
};

const permission: FeatureFlagsPermissionConditionConfig = {
  alias: "Umbricians.FeatureFlags.Condition.Permission",
  permission: "Umbricians.FeatureFlags.Manage",
};

const wrongPermission: FeatureFlagsPermissionConditionConfig = {
  alias: "Umbricians.FeatureFlags.Condition.Permission",
  // @ts-expect-error only the view and manage permissions exist
  permission: "Umbricians.FeatureFlags.Delete",
};

const wrongAlias: FeatureFlagsPermissionConditionConfig = {
  // @ts-expect-error the alias is fixed
  alias: "Umbricians.FeatureFlags.Condition.Other",
  permission: "Umbricians.FeatureFlags.View",
};

const entityType: FeatureFlagEntityType = "umbricians-feature-flags-flag";
// @ts-expect-error a typo in an entity type fails to compile
const typo: FeatureFlagEntityType = "umbricians-feature-flag";

export { editor, noName, permission, wrongPermission, wrongAlias, entityType, typo };
