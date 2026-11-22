import { css, customElement, html, nothing, repeat, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbCollectionViewElementBase, UMB_COLLECTION_CONTEXT } from "@umbraco-cms/backoffice/collection";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { sections, type FeatureFlagCollectionItem } from "../display.js";
import { changedText } from "../describe.js";
import { audienceNote, flagName, markerTags, stateTag } from "./flag-item.templates.js";
import type { FeatureFlagsCollectionFilter } from "./flags-collection.repository.js";
import "./flags-filters.element.js";
import "./flag-switch.element.js";
import "./flag-row-actions.element.js";

/** The flag list as cards, in its sections. */
@customElement("umbricians-feature-flags-card-view")
export class FeatureFlagsCardViewElement extends UmbCollectionViewElementBase<FeatureFlagCollectionItem> {
  @state()
  private _orderBy?: FeatureFlagsCollectionFilter["orderBy"];

  constructor() {
    super();
    this.consumeContext(UMB_COLLECTION_CONTEXT, (context) => {
      this.observe(context?.filter, (filter) => (this._orderBy = (filter as FeatureFlagsCollectionFilter | undefined)?.orderBy), "umbriciansFeatureFlagsOrder");
    });
  }

  override render() {
    return html`
      <umbricians-feature-flags-filters></umbricians-feature-flags-filters>
      ${sections(this._items, this._orderBy).map(
        (section) => html`
          ${this._orderBy === "updated" ? nothing : html`<h3>${section.name ?? this.localize.term("umbriciansFeatureFlags_noSection")}</h3>`}
          <div class="grid">${repeat(section.items, (item) => item.unique, (item) => this.#renderCard(item))}</div>
        `,
      )}
    `;
  }

  #renderCard(item: FeatureFlagCollectionItem) {
    return html`<uui-box>
      <div class="top">
        <div class="name">
          ${flagName(item, this._orderBy)}
          ${item.description ? html`<small>${item.description}</small>` : nothing}
        </div>
        <umbricians-feature-flags-switch .item=${item}></umbricians-feature-flags-switch>
        <umbricians-feature-flags-row-actions .name=${item.unique}></umbricians-feature-flags-row-actions>
      </div>
      <div class="row">
        ${stateTag(this.localize, item)} ${audienceNote(this.localize, item)}
      </div>
      <div class="row">
        ${markerTags(this.localize, item)}
        ${item.tags.map((tag) => html`<uui-tag look="placeholder">${tag}</uui-tag>`)}
      </div>
      <small class="changed">${changedText(this.localize, item)}</small>
    </uui-box>`;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
      }
      h3 {
        margin: var(--uui-size-space-5) 0 var(--uui-size-space-3);
      }
      h3:first-of-type {
        margin-top: 0;
      }
      .grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
        gap: var(--uui-size-space-4);
      }
      uui-box {
        --uui-box-default-padding: var(--uui-size-space-4);
      }
      .top {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-3);
      }
      .name {
        flex-grow: 1;
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
      }
      .name a {
        font-weight: bold;
      }
      .row {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2);
        margin-top: var(--uui-size-space-3);
      }
      small {
        color: var(--uui-color-text-alt);
      }
      .changed {
        display: block;
        margin-top: var(--uui-size-space-3);
        padding-top: var(--uui-size-space-3);
        border-top: 1px solid var(--uui-color-divider);
      }
    `,
  ];
}

export { FeatureFlagsCardViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-card-view": FeatureFlagsCardViewElement;
  }
}
