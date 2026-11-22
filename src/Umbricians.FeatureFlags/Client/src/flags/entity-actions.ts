import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { umbExtensionsRegistry } from "@umbraco-cms/backoffice/extension-registry";
import { UmbObserverController } from "@umbraco-cms/backoffice/observable-api";
import { FEATURE_FLAG_ENTITY_TYPE } from "../constants.js";

/**
 * Tells the host whether any entity action is registered for the flag entity type, now and whenever extensions change,
 * so a menu or column for them is shown only when it would have something in it.
 */
export function observeFlagEntityActions(host: UmbControllerHost, callback: (any: boolean) => void): UmbObserverController {
  return new UmbObserverController(
    host,
    umbExtensionsRegistry.byTypeAndFilter("entityAction", (manifest) => manifest.forEntityTypes.includes(FEATURE_FLAG_ENTITY_TYPE)),
    (manifests) => callback(manifests.length > 0),
    "umbriciansFlagEntityActions",
  );
}
