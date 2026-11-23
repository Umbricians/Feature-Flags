import { UmbConditionBase } from "@umbraco-cms/backoffice/extension-registry";
import type { UmbConditionConfigBase, UmbConditionControllerArguments } from "@umbraco-cms/backoffice/extension-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { FEATURE_FLAGS_PERMISSION_CONDITION_ALIAS } from "./constants.js";
import { observePermission } from "./permissions.js";

export interface FeatureFlagsPermissionConditionConfig
  extends UmbConditionConfigBase<typeof FEATURE_FLAGS_PERMISSION_CONDITION_ALIAS> {
  /** One of the FEATURE_FLAGS_PERMISSION_* verbs. */
  permission: string;
}

/**
 * Permits an extension when one of the current user's groups grants the given Feature Flags permission.
 */
export class FeatureFlagsPermissionCondition extends UmbConditionBase<FeatureFlagsPermissionConditionConfig> {
  constructor(host: UmbControllerHost, args: UmbConditionControllerArguments<FeatureFlagsPermissionConditionConfig>) {
    super(host, args);
    observePermission(this, this.config.permission, (permitted) => {
      this.permitted = permitted;
    });
  }
}

export { FeatureFlagsPermissionCondition as api };

declare global {
  interface UmbExtensionConditionConfigMap {
    umbriciansFeatureFlagsPermission: FeatureFlagsPermissionConditionConfig;
  }
}
