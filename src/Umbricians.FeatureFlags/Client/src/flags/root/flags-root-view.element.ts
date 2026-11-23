import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import type { UmbCollectionConfiguration } from "@umbraco-cms/backoffice/collection";
import { FEATURE_FLAGS_COLLECTION_ALIAS, FEATURE_FLAGS_PAGE_SIZE } from "../../constants.js";
import { FeatureFlagsSummaryContext } from "../summary.context.js";
import type { FeatureFlagSummary } from "../../api/feature-flags.resource.js";

/**
 * The flag list: which environment the editor is changing, whether the site has switched backoffice settings off, and
 * the collection of flags.
 */
@customElement("umbricians-feature-flags-root-view")
export class FeatureFlagsRootViewElement extends UmbLitElement {
  #config: UmbCollectionConfiguration = { pageSize: FEATURE_FLAGS_PAGE_SIZE };

  @state()
  private _summary?: FeatureFlagSummary;

  constructor() {
    super();
    const summary = new FeatureFlagsSummaryContext(this);
    this.observe(summary.summary, (value) => (this._summary = value));
  }

  override render() {
    return html`
      <div class="intro">
        ${this._summary
          ? html`<uui-tag color="warning" look="secondary" title=${this.localize.term("umbriciansFeatureFlags_environmentTitle")}>
              <uui-icon name="icon-globe"></uui-icon> ${this._summary.environment}
            </uui-tag>`
          : nothing}
        <p>${this.localize.term("umbriciansFeatureFlags_intro")}</p>
      </div>
      ${this._summary?.readOnly
        ? html`<uui-box class="read-only" headline=${this.localize.term("umbriciansFeatureFlags_readOnlyHeadline")}>
            ${this.localize.term("umbriciansFeatureFlags_readOnlyMessage")}
          </uui-box>`
        : nothing}
      <umb-collection alias=${FEATURE_FLAGS_COLLECTION_ALIAS} .config=${this.#config}></umb-collection>
    `;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
        height: 100%;
      }
      .intro {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-4);
        flex-wrap: wrap;
        padding: var(--uui-size-layout-1) var(--uui-size-layout-1) 0;
      }
      .intro p {
        margin: 0;
        color: var(--uui-color-text-alt);
      }
      .read-only {
        margin: var(--uui-size-space-4) var(--uui-size-layout-1) 0;
        --uui-box-default-padding: var(--uui-size-space-4);
      }
    `,
  ];
}

export { FeatureFlagsRootViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-root-view": FeatureFlagsRootViewElement;
  }
}
