import {
  FEATURE_FLAGS_PERMISSION_CONDITION_ALIAS,
  FEATURE_FLAGS_PERMISSION_ENTITY_TYPE,
  FEATURE_FLAGS_PERMISSION_MANAGE,
  FEATURE_FLAGS_PERMISSION_VIEW,
} from "./constants.js";

export const manifests: Array<UmbExtensionManifest> = [
  // Shown in Users → User groups → (group) → Permissions, in a "Feature Flags" box.
  {
    type: "entityUserPermission",
    alias: "Umbricians.FeatureFlags.UserPermission.View",
    name: "Feature Flags: View flags",
    weight: 200,
    forEntityTypes: [FEATURE_FLAGS_PERMISSION_ENTITY_TYPE],
    meta: {
      verbs: [FEATURE_FLAGS_PERMISSION_VIEW],
      label: "#umbriciansFeatureFlags_permissionView",
      description: "#umbriciansFeatureFlags_permissionViewDescription",
    },
  },
  {
    type: "entityUserPermission",
    alias: "Umbricians.FeatureFlags.UserPermission.Manage",
    name: "Feature Flags: Change flags",
    weight: 100,
    forEntityTypes: [FEATURE_FLAGS_PERMISSION_ENTITY_TYPE],
    meta: {
      verbs: [FEATURE_FLAGS_PERMISSION_MANAGE],
      label: "#umbriciansFeatureFlags_permissionManage",
      description: "#umbriciansFeatureFlags_permissionManageDescription",
    },
  },
  {
    type: "condition",
    alias: FEATURE_FLAGS_PERMISSION_CONDITION_ALIAS,
    name: "Feature Flags Permission Condition",
    api: () => import("./permission.condition.js"),
  },
];
