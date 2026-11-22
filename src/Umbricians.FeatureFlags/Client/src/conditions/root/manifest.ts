export const manifests: Array<UmbExtensionManifest> = [
  // Root: "Only on these sites", edited as a choice of the site's top-level content nodes.
  {
    type: "umbriciansFeatureFlagsCondition",
    alias: "Umbricians.FeatureFlags.Condition.Root",
    name: "Root Condition Editor",
    element: () => import("./root-condition.element.js"),
    meta: { name: "Umbricians.Root" },
  },
];
