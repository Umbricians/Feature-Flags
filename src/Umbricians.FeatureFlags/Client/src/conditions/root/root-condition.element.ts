import { css, customElement, html, nothing, property, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";
import { fetchRoots, type SiteRoot } from "../../api/feature-flags.resource.js";
import type { UmbriciansFeatureFlagsConditionElement } from "../condition.extension.js";
import { readRoots, rootRows, withRoot, type RootRow } from "./root.js";

/**
 * The editor of the Root condition (`Umbricians.Root`): "Only on these sites". It lists the site's roots, the top-level
 * content nodes, each with its domains, to choose from. A chosen root that has been deleted shows as "a deleted site"
 * with a button to remove it.
 * It takes the condition's parameters in `value` and, when an editor changes them, sets `value` and fires `change`.
 */
@customElement("umbricians-feature-flags-root-condition")
export class RootConditionElement extends UmbLitElement implements UmbriciansFeatureFlagsConditionElement {
  /** The condition's parameters: `{ "Roots": [ "<key>", ... ] }`. */
  @property({ attribute: false })
  value: Record<string, unknown> = {};

  @property({ type: Boolean, reflect: true })
  readonly = false;

  /** Null until the roots have loaded, and for good if they fail to: then no chosen root is called deleted. */
  @state() private _roots: Array<SiteRoot> | null = null;
  @state() private _failed = false;

  override connectedCallback() {
    super.connectedCallback();
    fetchRoots().then(
      (roots) => (this._roots = roots),
      () => (this._failed = true),
    );
  }

  #choose(key: string, chosen: boolean) {
    this.value = withRoot(this.value, key, chosen);
    this.dispatchEvent(new UmbChangeEvent());
  }

  override render() {
    const rows = rootRows(readRoots(this.value), this._roots);
    return html`<p class="summary">${this.localize.term("umbriciansFeatureFlags_onOnlyOnSites")}</p>
      ${this._failed ? html`<p class="problem">${this.localize.term("umbriciansFeatureFlags_rootsFailed")}</p>` : nothing}
      ${this._roots === null && !this._failed ? html`<p>${this.localize.term("umbriciansFeatureFlags_loading")}</p>` : nothing}
      ${rows.length === 0 && this._roots !== null ? html`<p>${this.localize.term("umbriciansFeatureFlags_noRoots")}</p>` : nothing}
      ${rows.map((row) => this.#renderRow(row))}`;
  }

  #renderRow(row: RootRow) {
    const name = row.name ?? (row.deleted ? this.localize.term("umbriciansFeatureFlags_deletedSite") : row.key);
    return html`<div class="row">
      <uui-checkbox
        label=${name}
        ?checked=${row.selected}
        ?disabled=${this.readonly || row.deleted || this._roots === null}
        @change=${(e: Event) => {
          e.stopPropagation();
          this.#choose(row.key, (e.target as HTMLInputElement).checked);
        }}>
        <span class=${row.deleted ? "problem" : ""}>${name}</span>
        ${row.domains.length ? html`<small>${row.domains.join(", ")}</small>` : nothing}
      </uui-checkbox>
      ${row.deleted
        ? html`<uui-button
            compact
            look="secondary"
            label=${this.localize.term("umbriciansFeatureFlags_removeGroup", name)}
            ?disabled=${this.readonly}
            @click=${() => this.#choose(row.key, false)}>
            ${this.localize.term("umbriciansFeatureFlags_remove")}
          </uui-button>`
        : nothing}
    </div>`;
  }

  static override styles = css`
    :host {
      display: block;
    }
    .summary {
      font-weight: bold;
      margin-top: 0;
    }
    .row {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-4);
      margin-bottom: var(--uui-size-space-2);
    }
    small {
      color: var(--uui-color-text-alt);
      margin-left: var(--uui-size-space-3);
    }
    .problem {
      color: var(--uui-color-danger-standalone);
    }
  `;
}

export { RootConditionElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-root-condition": RootConditionElement;
  }
}
