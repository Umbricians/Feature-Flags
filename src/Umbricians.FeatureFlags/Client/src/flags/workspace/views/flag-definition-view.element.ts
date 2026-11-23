import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { FeatureFlag } from "../../../api/feature-flags.resource.js";
import { FEATURE_FLAG_WORKSPACE_CONTEXT } from "../flag-workspace.context-token.js";

/**
 * The flag editor's Definition tab: the saved setting exactly as it is stored, in Microsoft's feature_management JSON,
 * which is what Microsoft.FeatureManagement reads. Read-only, and as last saved.
 */
@customElement("umbricians-feature-flags-definition-view")
export class FeatureFlagDefinitionViewElement extends UmbLitElement {
  @state() private _flag?: FeatureFlag;

  constructor() {
    super();
    this.consumeContext(FEATURE_FLAG_WORKSPACE_CONTEXT, (context) => {
      this.observe(context?.flag, (flag) => (this._flag = flag), "umbriciansFlag");
    });
  }

  override render() {
    if (!this._flag) return nothing;
    const definition = this._flag.override?.definition;
    return html`<uui-box headline=${this.localize.term("umbriciansFeatureFlags_definition")}>
      ${definition
        ? html`<p class="hint">${this.localize.term("umbriciansFeatureFlags_definitionHelp")}</p>
            <pre>${definition}</pre>`
        : html`<p class="hint">${this.localize.term("umbriciansFeatureFlags_definitionDefault")}</p>`}
    </uui-box>`;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
        padding: var(--uui-size-layout-1);
      }
      .hint {
        color: var(--uui-color-text-alt);
        margin-top: 0;
      }
      pre {
        margin: 0;
        padding: var(--uui-size-space-4);
        background: var(--uui-color-surface-alt);
        border-radius: var(--uui-border-radius);
        overflow-x: auto;
        font-size: 13px;
      }
    `,
  ];
}

export { FeatureFlagDefinitionViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-definition-view": FeatureFlagDefinitionViewElement;
  }
}
