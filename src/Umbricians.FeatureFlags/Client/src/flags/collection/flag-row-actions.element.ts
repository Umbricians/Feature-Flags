import { customElement, html, property } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbEntityContext } from "@umbraco-cms/backoffice/entity";
import { flagEntity } from "../entity.js";

/**
 * The entity actions developers registered for the flag entity type, as a button on a flag's row or card. It gives
 * Umbraco's own actions bundle the flag's entity type and name, and shows nothing when there are no actions.
 */
@customElement("umbricians-feature-flags-row-actions")
export class FeatureFlagRowActionsElement extends UmbLitElement {
  #entity = new UmbEntityContext(this);

  @property()
  set name(name: string) {
    const { entityType, unique } = flagEntity(name);
    this.#entity.setEntityType(entityType);
    this.#entity.setUnique(unique);
    this.#name = name;
  }
  get name() {
    return this.#name;
  }
  #name = "";

  override render() {
    return html`<umb-entity-actions-bundle label=${this.#name}></umb-entity-actions-bundle>`;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-row-actions": FeatureFlagRowActionsElement;
  }
}
