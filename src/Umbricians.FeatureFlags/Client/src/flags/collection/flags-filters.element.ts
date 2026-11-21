import { css, customElement, html, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_COLLECTION_CONTEXT } from "@umbraco-cms/backoffice/collection";
import type { UUISelectEvent } from "@umbraco-cms/backoffice/external/uui";
import { FEATURE_FLAGS_SUMMARY_CONTEXT } from "../summary.context.js";
import type { FeatureFlagSummary } from "../../api/feature-flags.resource.js";
import type { FeatureFlagsCollectionFilter } from "./flags-collection.repository.js";

type Option = { name: string; value: string; selected?: boolean };

/**
 * The flag list's filters beside Umbraco's own search: state, what to show, group, tag and order. Changing one goes
 * back to the first page, since the collection reloads from the server.
 */
@customElement("umbricians-feature-flags-filters")
export class FeatureFlagsFiltersElement extends UmbLitElement {
  #collection?: typeof UMB_COLLECTION_CONTEXT.TYPE;

  @state()
  private _filter: FeatureFlagsCollectionFilter = {};

  @state()
  private _summary?: FeatureFlagSummary;

  constructor() {
    super();
    this.consumeContext(UMB_COLLECTION_CONTEXT, (context) => {
      this.#collection = context;
      this.observe(context?.filter, (filter) => (this._filter = (filter ?? {}) as FeatureFlagsCollectionFilter), "umbriciansFeatureFlagsFilter");
    });
    this.consumeContext(FEATURE_FLAGS_SUMMARY_CONTEXT, (context) => {
      this.observe(context?.summary, (summary) => (this._summary = summary), "umbriciansFeatureFlagsSummary");
    });
  }

  #set(change: Partial<FeatureFlagsCollectionFilter>) {
    this.#collection?.pagination.setCurrentPageNumber(1);
    this.#collection?.setFilter({ ...change, skip: 0 } as Partial<FeatureFlagsCollectionFilter>);
  }

  #options(options: Array<Option>, current: string | undefined | null): Array<Option> {
    return options.map((option) => ({ ...option, selected: option.value === (current ?? "") }));
  }

  override render() {
    const term = (key: string) => this.localize.term(key);
    const groups: Array<Option> = [
      { name: term("umbriciansFeatureFlags_allGroups"), value: "*" },
      ...(this._summary?.groups ?? []).map((group) => ({
        name: `${group.name ?? term("umbriciansFeatureFlags_ungrouped")} (${group.count})`,
        value: group.name ?? "",
      })),
    ];
    const tags: Array<Option> = [
      { name: term("umbriciansFeatureFlags_allTags"), value: "" },
      ...(this._summary?.tags ?? []).map((tag) => ({ name: tag, value: tag })),
    ];

    return html`
      <uui-select
        label=${term("umbriciansFeatureFlags_filterState")}
        .options=${this.#options(
          [
            { name: term("umbriciansFeatureFlags_allStates"), value: "" },
            { name: term("umbriciansFeatureFlags_stateOff"), value: "off" },
            { name: term("umbriciansFeatureFlags_stateEveryone"), value: "everyone" },
            { name: term("umbriciansFeatureFlags_stateAudience"), value: "audience" },
            { name: term("umbriciansFeatureFlags_stateCustom"), value: "custom" },
          ],
          this._filter.state,
        )}
        @change=${(e: UUISelectEvent) => this.#set({ state: e.target.value as FeatureFlagsCollectionFilter["state"] })}></uui-select>
      <uui-select
        label=${term("umbriciansFeatureFlags_filterShow")}
        .options=${this.#options(
          [
            { name: term("umbriciansFeatureFlags_showDeclared"), value: "" },
            { name: term("umbriciansFeatureFlags_showChanged"), value: "changed" },
            { name: term("umbriciansFeatureFlags_showDiffers"), value: "differs" },
            { name: term("umbriciansFeatureFlags_showTwice"), value: "twice" },
            {
              name: this.localize.term("umbriciansFeatureFlags_showUndeclared", this._summary?.undeclared ?? 0),
              value: "undeclared",
            },
          ],
          this._filter.show === "declared" ? "" : this._filter.show,
        )}
        @change=${(e: UUISelectEvent) => this.#set({ show: (e.target.value || undefined) as FeatureFlagsCollectionFilter["show"] })}></uui-select>
      <uui-select
        label=${term("umbriciansFeatureFlags_filterGroup")}
        .options=${this.#options(groups, this._filter.group === undefined || this._filter.group === null ? "*" : this._filter.group)}
        @change=${(e: UUISelectEvent) => this.#set({ group: e.target.value === "*" ? null : String(e.target.value) })}></uui-select>
      <uui-select
        label=${term("umbriciansFeatureFlags_filterTag")}
        .options=${this.#options(tags, this._filter.tag)}
        @change=${(e: UUISelectEvent) => this.#set({ tag: String(e.target.value) || undefined })}></uui-select>
      <uui-select
        label=${term("umbriciansFeatureFlags_sort")}
        .options=${this.#options(
          [
            { name: term("umbriciansFeatureFlags_sortName"), value: "" },
            { name: term("umbriciansFeatureFlags_sortUpdated"), value: "updated" },
          ],
          this._filter.orderBy === "group" ? "" : this._filter.orderBy,
        )}
        @change=${(e: UUISelectEvent) => this.#set({ orderBy: (e.target.value || undefined) as FeatureFlagsCollectionFilter["orderBy"] })}></uui-select>
    `;
  }

  static override styles = css`
    :host {
      display: flex;
      flex-wrap: wrap;
      gap: var(--uui-size-space-3);
      margin-bottom: var(--uui-size-space-5);
    }
  `;
}

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-filters": FeatureFlagsFiltersElement;
  }
}
