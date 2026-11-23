// Example developer extension for Feature Flags: plain JavaScript, so the demo needs no client build. Each manifest
// uses only what docs/backoffice.md and docs/conditions.md promise.
export const manifests = [
  // The editor for the Demo.Culture condition. meta.name is the C# condition's Name.
  {
    type: "umbriciansFeatureFlagsCondition",
    alias: "FeatureFlagsDemo.Condition.Culture",
    name: "Culture Condition Editor",
    element: () => import("./culture-condition.element.js"),
    meta: { name: "Demo.Culture" },
  },

  // An entity action on a flag: in the flag workspace's actions menu and on each flag in the list.
  {
    type: "entityAction",
    kind: "default",
    alias: "FeatureFlagsDemo.EntityAction.CopyName",
    name: "Copy flag name",
    forEntityTypes: ["umbricians-feature-flags-flag"],
    api: () => import("./copy-name.action.js"),
    meta: { icon: "icon-documents", label: "Copy name" },
  },

  // A workspace view on the flag workspace, which reads the flag from the public flag workspace context.
  {
    type: "workspaceView",
    alias: "FeatureFlagsDemo.WorkspaceView.About",
    name: "About this flag",
    element: () => import("./flag-about.element.js"),
    weight: 50,
    meta: { label: "About", pathname: "about", icon: "icon-info" },
    conditions: [
      { alias: "Umb.Condition.WorkspaceAlias", match: "Umbricians.FeatureFlags.Workspace.Flag" },
      { alias: "Umbricians.FeatureFlags.Condition.Permission", permission: "Umbricians.FeatureFlags.View" },
    ],
  },
];
