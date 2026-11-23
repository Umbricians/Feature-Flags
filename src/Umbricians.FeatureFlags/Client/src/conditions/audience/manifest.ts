export const manifests: Array<UmbExtensionManifest> = [
  // The audience: Microsoft's targeting filter, edited as members, member groups and groups from group providers.
  {
    type: "umbriciansFeatureFlagsCondition",
    alias: "Umbricians.FeatureFlags.Condition.Audience",
    name: "Audience Condition Editor",
    element: () => import("./audience-condition.element.js"),
    meta: { name: "Microsoft.Targeting" },
  },
];
