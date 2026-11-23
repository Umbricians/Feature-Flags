// A condition editor's element, for consumer.check.ts to import.
import type { UmbriciansFeatureFlagsConditionElement } from "../index.d.ts";

export class CultureElement extends HTMLElement implements UmbriciansFeatureFlagsConditionElement {
  value: Record<string, unknown> = {};
}

export { CultureElement as element };
