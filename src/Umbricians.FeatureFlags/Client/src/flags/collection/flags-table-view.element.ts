import { css, customElement, html, nothing, repeat, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbCollectionViewElementBase, UMB_COLLECTION_CONTEXT } from "@umbraco-cms/backoffice/collection";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { sections, shortName, stateColor, type FeatureFlagCollectionItem } from "../display.js";
import { changedText, describeAudience, markers, stateLabel } from "../describe.js";
import { editFlagPath } from "../paths.js";
import type { FeatureFlagsCollectionFilter } from "./flags-collection.repository.js";
import "./flags-filters.element.js";
import "./flag-switch.element.js";

/** The flag list as a table, in its sections. */
@customElement("umbricians-feature-flags-table-view")
export class FeatureFlagsTableViewElement extends UmbCollectionViewElementBase<FeatureFlagCollectionItem> {
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
      <uui-box>
        <uui-table aria-label=${this.localize.term("umbriciansFeatureFlags_name")}>
          <uui-table-head>
            <uui-table-head-cell>${this.localize.term("umbriciansFeatureFlags_columnFlag")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("umbriciansFeatureFlags_columnState")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("umbriciansFeatureFlags_columnSwitch")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("umbriciansFeatureFlags_columnNotes")}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term("umbriciansFeatureFlags_columnChanged")}</uui-table-head-cell>
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
                  </uui-table-row>`}
              ${repeat(section.items, (item) => item.unique, (item) => this.#renderRow(item))}
            `,
          )}
        </uui-table>
      </uui-box>
    `;
  }

  #renderRow(item: FeatureFlagCollectionItem) {
    const audience = item.audience ? describeAudience(this.localize, item.audience) : "";
    return html`<uui-table-row>
      <uui-table-cell>
        <div class="name">
          ${item.isDeclared
            ? html`<a href=${editFlagPath(item.name)} title=${item.name}>${this._orderBy === "updated" ? item.name : shortName(item)}</a>`
            : html`<strong title=${item.name}>${item.name}</strong>`}
          ${item.description ? html`<small>${item.description}</small>` : nothing}
          ${item.tags.length ? html`<div class="tags">${item.tags.map((tag) => html`<uui-tag look="placeholder">${tag}</uui-tag>`)}</div>` : nothing}
        </div>
      </uui-table-cell>
      <uui-table-cell>
        <uui-tag color=${stateColor(item.state)} look=${item.state === "off" ? "secondary" : "primary"}>${stateLabel(this.localize, item.state)}</uui-tag>
        ${audience
          ? html`<small class="audience">${item.state === "off" ? this.localize.term("umbriciansFeatureFlags_keeps", audience) : audience}</small>`
          : nothing}
      </uui-table-cell>
      <uui-table-cell><umbricians-feature-flag-switch .item=${item}></umbricians-feature-flag-switch></uui-table-cell>
      <uui-table-cell>
        <div class="markers">
          ${markers(this.localize, item).map((marker) => html`<uui-tag color=${marker.color} look="outline">${marker.label}</uui-tag>`)}
        </div>
      </uui-table-cell>
      <uui-table-cell><small>${changedText(this.localize, item)}</small></uui-table-cell>
    </uui-table-row>`;
  }

  static override styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
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
