// A workspace view on a flag. It reads the flag from the public flag workspace context, which has no constant to
// import: a token built from the documented alias is the same token.
import { css, html } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbContextToken } from "@umbraco-cms/backoffice/context-api";

const FLAG_CONTEXT = new UmbContextToken("Umbricians.FeatureFlags.FlagWorkspaceContext.Public");

export class DemoFlagAboutElement extends UmbLitElement {
  static properties = { _flag: { state: true } };

  static styles = css`
    :host {
      display: block;
      padding: var(--uui-size-layout-1);
    }
  `;

  constructor() {
    super();
    this.consumeContext(FLAG_CONTEXT, (context) => this.observe(context?.flag, (flag) => (this._flag = flag)));
  }

  render() {
    if (!this._flag) return html`<uui-box>Loading…</uui-box>`;
    return html`<uui-box headline=${this._flag.name}>
      <p>State: <strong>${this._flag.state}</strong></p>
      <p>${this._flag.summary ?? "No conditions."}</p>
    </uui-box>`;
  }
}

customElements.define("feature-flags-demo-flag-about", DemoFlagAboutElement);

export { DemoFlagAboutElement as element };
