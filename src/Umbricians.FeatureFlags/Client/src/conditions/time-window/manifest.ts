export const manifests: Array<UmbExtensionManifest> = [
  // The time window: Microsoft's time window filter, edited as "on from ... until ...", either end optional.
  {
    type: "umbriciansFeatureFlagsCondition",
    alias: "Umbricians.FeatureFlags.Condition.TimeWindow",
    name: "Time Window Condition Editor",
    element: () => import("./time-window-condition.element.js"),
    meta: { name: "Microsoft.TimeWindow" },
  },
];
