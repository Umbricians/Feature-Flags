import { css, customElement, html, nothing, repeat, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbCollectionViewElementBase, UMB_COLLECTION_CONTEXT } from "@umbraco-cms/backoffice/collection";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { sections, type FeatureFlagCollectionItem } from "../display.js";
import { changedText } from "../describe.js";
import { observeFlagEntityActions } from "../entity-actions.js";
import { audienceNote, flagName, markerTags, stateTag } from "./flag-item.templates.js";
import type { FeatureFlagsCollectionFilter } from "./flags-collection.repository.js";
import "./flags-filters.element.js";
import "./flag-switch.element.js";
import "./flag-row-actions.element.js";

/** The flag list as a table, in its sections. */
@customElement("umbricians-feature-flags-table-view")
export class FeatureFlagsTableViewElement extends UmbCollectionViewElementBase<FeatureFlagCollectionItem> {
  @state()
  private _orderBy?: FeatureFlagsCollectionFilter["orderBy"];

  @state() private _hasEntityActions = false;

  constructor() {
    super();
    observeFlagEntityActions(this, (any) => (this._hasEntityActions = any));
    this.consumeContext(UMB_COLLECTION_CONTEXT, (context) => {
      this.observe(context?.filter, (filter) => (this._orderBy = (filter as FeatureFlagsCollectionFilter | undefined)?.orderBy), "umbriciansFeatureFlagsOrder");
    });
  }

  override render() {
    return html`
      <umbricians-feature-flags-filters></umbricians-feature-flags-filters>
      <uui-box>
        <uui-table aria-label=${this.localize.term("umbriciansFeatureFlags_name")}>
          <uui-table-head>
            <uui-table-head-cell>${this.localize.term("umbriciansFeatureFlags_columnFlag")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("umbriciansFeatureFlags_columnState")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("umbriciansFeatureFlags_columnSwitch")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("umbriciansFeatureFlags_columnNotes")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("umbriciansFeatureFlags_columnChanged")}</uui-table-head-cell>
            ${this._hasEntityActions
              ? html`<uui-table-head-cell><span class="visually-hidden">${this.localize.term("umbriciansFeatureFlags_columnActions")}</span></uui-table-head-cell>`
              : nothing}
          </uui-table-head>
          ${sections(this._items, this._orderBy).map(
            (section) => html`
              ${this._orderBy === "updated"
                ? nothing
                : html`<uui-table-row class="section">
                    <uui-table-cell>${section.name ?? this.localize.term("umbriciansFeatureFlags_noSection")}</uui-table-cell>
                    <uui-table-cell></uui-table-cell>
                    <uui-table-cell></uui-table-cell>
                    <uui-table-cell></uui-table-cell>
                    <uui-table-cell></uui-table-cell>
                    ${this._hasEntityActions ? html`<uui-table-cell></uui-table-cell>` : nothing}
                  </uui-table-row>`}
              ${repeat(section.items, (item) => item.unique, (item) => this.#renderRow(item))}
            `,
          )}
        </uui-table>
      </uui-box>
    `;
  }

  #renderRow(item: FeatureFlagCollectionItem) {
    return html`<uui-table-row>
      <uui-table-cell>
        <div class="name">
          ${flagName(item, this._orderBy)}
          ${item.description ? html`<small>${item.description}</small>` : nothing}
          ${item.tags.length ? html`<div class="tags">${item.tags.map((tag) => html`<uui-tag look="placeholder">${tag}</uui-tag>`)}</div>` : nothing}
        </div>
      </uui-table-cell>
      <uui-table-cell>
        ${stateTag(this.localize, item)} ${audienceNote(this.localize, item, "audience")}
      </uui-table-cell>
      <uui-table-cell><umbricians-feature-flags-switch .item=${item}></umbricians-feature-flags-switch></uui-table-cell>
      <uui-table-cell>
        <div class="markers">
          ${markerTags(this.localize, item)}
        </div>
      </uui-table-cell>
      <uui-table-cell><small>${changedText(this.localize, item)}</small></uui-table-cell>
      ${this._hasEntityActions
        ? html`<uui-table-cell><umbricians-feature-flags-row-actions .name=${item.unique}></umbricians-feature-flags-row-actions></uui-table-cell>`
        : nothing}
    </uui-table-row>`;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
      }
      /* Fixed columns, so a row that gains notes doesn't move the others. The first takes what is left. */
      uui-table {
        table-layout: fixed;
      }
      uui-table-head-cell:nth-child(2) {
        width: 20%;
      }
      uui-table-head-cell:nth-child(3) {
        width: 7em;
      }
      uui-table-head-cell:nth-child(4) {
        width: 18%;
      }
      uui-table-head-cell:nth-child(5) {
        width: 14%;
      }
      uui-table-head-cell:nth-child(6) {
        width: 5em;
      }
      .visually-hidden {
        position: absolute;
        width: 1px;
        height: 1px;
        overflow: hidden;
        clip-path: inset(50%);
        white-space: nowrap;
      }
      uui-table-row.section uui-table-cell {
        font-weight: bold;
        background: var(--uui-color-surface-alt);
      }
      .name {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
      }
      .name a {
        font-weight: bold;
      }
      small {
        color: var(--uui-color-text-alt);
      }
      .audience {
        display: block;
        margin-top: var(--uui-size-space-1);
      }
      .tags,
      .markers {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-1);
      }
    `,
  ];
}

export { FeatureFlagsTableViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-table-view": FeatureFlagsTableViewElement;
  }
}
