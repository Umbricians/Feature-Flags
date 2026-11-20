import { manifests as flags } from "./flags/manifest.js";
import { manifests as permissions } from "./permissions/manifest.js";
import { manifests as localization } from "./localization/manifest.js";

// Loaded from umbraco-package.json; collates every manifest in the package, one manifest.ts per folder.
export const manifests: Array<UmbExtensionManifest> = [...flags, ...permissions, ...localization];
