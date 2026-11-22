import type { ManifestElement } from "@umbraco-cms/backoffice/extension-api";

/**
 * The element of a condition's editor. It is given the condition's parameters as a JSON object in `value`, and
 * `readonly` when the editor may not change the flag. When the editor changes the parameters it sets `value` to the new
 * JSON object and dispatches a `change` event (Umbraco's `UmbChangeEvent`, which bubbles); the flag editor then reads
 * `element.value`. The `value` it is given is frozen (deep-frozen by `UmbObjectState`), so an element must never change
 * it in place: it fires `change` with a new object. Nothing else is promised.
 */
export interface UmbriciansFeatureFlagsConditionElement extends HTMLElement {
  value: Record<string, unknown>;
  readonly?: boolean;
}

/** The editor for one of a flag's conditions, found by `meta.name`, the name of the C# `IFeatureFlagCondition`. */
export interface ManifestUmbriciansFeatureFlagsCondition extends ManifestElement<UmbriciansFeatureFlagsConditionElement> {
  type: "umbriciansFeatureFlagsCondition";
  meta: MetaUmbriciansFeatureFlagsCondition;
}

export interface MetaUmbriciansFeatureFlagsCondition {
  /** The `Name` of the condition's C# class, such as `Contoso.Culture`. Matched ignoring case, like Microsoft's filter names. */
  name: string;
}

declare global {
  interface UmbExtensionManifestMap {
    umbriciansFeatureFlagsCondition: ManifestUmbriciansFeatureFlagsCondition;
  }
}
