import {
  FEATURE_FLAGS_COLLECTION_ALIAS,
  FEATURE_FLAGS_COLLECTION_REPOSITORY_ALIAS,
  FEATURE_FLAGS_ROOT_ENTITY_TYPE,
  FEATURE_FLAGS_ROOT_WORKSPACE_ALIAS,
  FEATURE_FLAG_ENTITY_TYPE,
  FEATURE_FLAG_WORKSPACE_ALIAS,
  UMB_COLLECTION_CONDITION,
  UMB_WORKSPACE_CONDITION,
} from "../constants.js";
import { FEATURE_FLAGS_PERMISSION_CONDITION_ALIAS, FEATURE_FLAGS_PERMISSION_VIEW } from "../permissions/constants.js";

export const manifests: Array<UmbExtensionManifest> = [
  // Settings → Advanced → Feature Flags, which opens the flag list.
  {
    type: "menuItem",
    alias: "Umbricians.FeatureFlags.MenuItem.Root",
    name: "Feature Flags Menu Item",
    weight: 50,
    meta: {
      label: "#umbriciansFeatureFlags_name",
      icon: "icon-flag",
      entityType: FEATURE_FLAGS_ROOT_ENTITY_TYPE,
      menus: ["Umb.Menu.AdvancedSettings"],
    },
    conditions: [{ alias: FEATURE_FLAGS_PERMISSION_CONDITION_ALIAS, permission: FEATURE_FLAGS_PERMISSION_VIEW }],
  },

  // The flag list: a workspace for the menu item, whose one view holds the collection.
  {
    type: "workspace",
    kind: "default",
    alias: FEATURE_FLAGS_ROOT_WORKSPACE_ALIAS,
    name: "Feature Flags Root Workspace",
    meta: {
      entityType: FEATURE_FLAGS_ROOT_ENTITY_TYPE,
      headline: "#umbriciansFeatureFlags_name",
    },
  },
  {
    type: "workspaceView",
    alias: "Umbricians.FeatureFlags.WorkspaceView.Root.Flags",
    name: "Feature Flags Root Workspace Flags View",
    element: () => import("./root/flags-root-view.element.js"),
    weight: 100,
    meta: {
      label: "#umbriciansFeatureFlags_flags",
      pathname: "flags",
      icon: "icon-flag",
    },
    conditions: [{ alias: UMB_WORKSPACE_CONDITION, match: FEATURE_FLAGS_ROOT_WORKSPACE_ALIAS }],
  },

  {
    type: "collection",
    kind: "default",
    alias: FEATURE_FLAGS_COLLECTION_ALIAS,
    name: "Feature Flags Collection",
    meta: { repositoryAlias: FEATURE_FLAGS_COLLECTION_REPOSITORY_ALIAS },
  },
  {
    type: "repository",
    alias: FEATURE_FLAGS_COLLECTION_REPOSITORY_ALIAS,
    name: "Feature Flags Collection Repository",
    api: () => import("./collection/flags-collection.repository.js"),
  },
  {
    type: "collectionTextFilter",
    kind: "default",
    alias: "Umbricians.FeatureFlags.CollectionTextFilter",
    name: "Feature Flags Search",
    conditions: [{ alias: UMB_COLLECTION_CONDITION, match: FEATURE_FLAGS_COLLECTION_ALIAS }],
  },
  {
    type: "collectionView",
    alias: "Umbricians.FeatureFlags.CollectionView.Table",
    name: "Feature Flags List View",
    element: () => import("./collection/flags-table-view.element.js"),
    weight: 200,
    meta: { label: "#umbriciansFeatureFlags_listView", icon: "icon-list", pathName: "list" },
    conditions: [{ alias: UMB_COLLECTION_CONDITION, match: FEATURE_FLAGS_COLLECTION_ALIAS }],
  },
  {
    type: "collectionView",
    alias: "Umbricians.FeatureFlags.CollectionView.Cards",
    name: "Feature Flags Card View",
    element: () => import("./collection/flags-card-view.element.js"),
    weight: 100,
    meta: { label: "#umbriciansFeatureFlags_cardView", icon: "icon-grid", pathName: "cards" },
    conditions: [{ alias: UMB_COLLECTION_CONDITION, match: FEATURE_FLAGS_COLLECTION_ALIAS }],
  },

  // One flag's editor, at …/workspace/umbricians-feature-flags-flag/edit/{name}.
  {
    type: "workspace",
    kind: "routable",
    alias: FEATURE_FLAG_WORKSPACE_ALIAS,
    name: "Feature Flag Workspace",
    api: () => import("./workspace/flag-workspace.context.js"),
    meta: { entityType: FEATURE_FLAG_ENTITY_TYPE },
  },
  // The editor's tabs, like the Log Viewer's Overview and Search.
  {
    type: "workspaceView",
    alias: "Umbricians.FeatureFlags.WorkspaceView.Flag.Settings",
    name: "Feature Flag Settings View",
    element: () => import("./workspace/views/flag-settings-view.element.js"),
    weight: 200,
    meta: {
      label: "#general_settings",
      pathname: "settings",
      icon: "icon-settings",
    },
    conditions: [{ alias: UMB_WORKSPACE_CONDITION, match: FEATURE_FLAG_WORKSPACE_ALIAS }],
  },
  {
    type: "workspaceView",
    alias: "Umbricians.FeatureFlags.WorkspaceView.Flag.Definition",
    name: "Feature Flag Definition View",
    element: () => import("./workspace/views/flag-definition-view.element.js"),
    weight: 100,
    meta: {
      label: "#umbriciansFeatureFlags_definition",
      pathname: "definition",
      icon: "icon-code",
    },
    conditions: [{ alias: UMB_WORKSPACE_CONDITION, match: FEATURE_FLAG_WORKSPACE_ALIAS }],
  },
];
