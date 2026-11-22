// The Demo.Culture condition's editor. The contract: `value` (the parameters) and `readonly` in; when the editor
// changes the parameters it sets `value` and dispatches an UmbChangeEvent.
import { html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";

export class DemoCultureConditionElement extends UmbLitElement {
  static properties = {
    /** The condition's parameters: { "Cultures": [ "en-GB", "fr" ] }. */
    value: { attribute: false },
    readonly: { type: Boolean },
  };

  constructor() {
    super();
    this.value = {};
    this.readonly = false;
  }

  #change(event) {
    event.stopPropagation(); // the input's own change isn't the element's
    const text = event.target.value;
    this.value = { Cultures: text.split(",").map((culture) => culture.trim()).filter(Boolean) };
    this.dispatchEvent(new UmbChangeEvent());
  }

  render() {
    return html`<uui-input
      label="Cultures"
      placeholder="en-GB, fr"
      .value=${(this.value?.Cultures ?? []).join(", ")}
      ?readonly=${this.readonly}
      @change=${this.#change}></uui-input>`;
  }
}

customElements.define("feature-flags-demo-culture-condition", DemoCultureConditionElement);

export { DemoCultureConditionElement as element };
