export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "localization",
    alias: "Umbricians.FeatureFlags.Localization.En",
    name: "Umbricians Feature Flags English",
    meta: { culture: "en" },
    js: () => import("./en.js"),
  },
];
