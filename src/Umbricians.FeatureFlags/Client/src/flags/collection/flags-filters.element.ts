import { css, customElement, html, nothing, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UMB_COLLECTION_CONTEXT } from "@umbraco-cms/backoffice/collection";
import type { UUIBooleanInputEvent, UUISelectEvent } from "@umbraco-cms/backoffice/external/uui";
import { FEATURE_FLAGS_SUMMARY_CONTEXT } from "../summary.context.js";
import type { FeatureFlagSummary } from "../../api/feature-flags.resource.js";
import type { FlagMark } from "../../api/types.js";
import { ALL_SECTIONS, sectionFromOption, sectionOption } from "../display.js";
import type { FeatureFlagsCollectionFilter } from "./flags-collection.repository.js";

type Option = { name: string; value: string; selected?: boolean };

/**
 * The flag list's filters beside Umbraco's own search: state, which list to show, section, tag and order, and marks a
 * flag must have, which combine. Changing one goes back to the first page, since the collection reloads from the
 * server.
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

  #toggleMark(mark: FlagMark, on: boolean) {
    const marks = new Set(this._filter.marks ?? []);
    if (on) marks.add(mark);
    else marks.delete(mark);
    this.#set({ marks: [...marks] });
  }

  #options(options: Array<Option>, current: string | undefined | null): Array<Option> {
    return options.map((option) => ({ ...option, selected: option.value === (current ?? "") }));
  }

  override render() {
    const term = (key: string) => this.localize.term(key);
    const sectionOptions: Array<Option> = [
      { name: term("umbriciansFeatureFlags_allSections"), value: ALL_SECTIONS },
      ...(this._summary?.sections ?? []).map((section) => ({
        name: `${section.name ?? term("umbriciansFeatureFlags_noSection")} (${section.count})`,
        value: section.name ?? "",
      })),
    ];
    const tags: Array<Option> = [
      { name: term("umbriciansFeatureFlags_allTags"), value: "" },
      ...(this._summary?.tags ?? []).map((tag) => ({ name: tag, value: tag })),
    ];

    const marks: Array<[FlagMark, string]> = [
      ["changed", "umbriciansFeatureFlags_showChanged"],
      ["differs", "umbriciansFeatureFlags_showDiffers"],
      ["twice", "umbriciansFeatureFlags_showTwice"],
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
            { name: term("umbriciansFeatureFlags_stateConditions"), value: "conditions" },
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
            {
              name: this.localize.term("umbriciansFeatureFlags_showUndeclared", this._summary?.undeclared ?? 0),
              value: "undeclared",
            },
          ],
          this._filter.show === "declared" ? "" : this._filter.show,
        )}
        @change=${(e: UUISelectEvent) => this.#set({ show: (e.target.value || undefined) as FeatureFlagsCollectionFilter["show"] })}></uui-select>
      <uui-select
        label=${term("umbriciansFeatureFlags_filterSection")}
        .options=${this.#options(sectionOptions, sectionOption(this._filter.section))}
        @change=${(e: UUISelectEvent) => this.#set({ section: sectionFromOption(String(e.target.value)) })}></uui-select>
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
          this._filter.orderBy === "section" ? "" : this._filter.orderBy,
        )}
        @change=${(e: UUISelectEvent) => this.#set({ orderBy: (e.target.value || undefined) as FeatureFlagsCollectionFilter["orderBy"] })}></uui-select>
      ${this._filter.show === "undeclared"
        ? nothing
        : html`<div class="marks" role="group" aria-label=${term("umbriciansFeatureFlags_filterMarks")}>
            ${marks.map(
              ([mark, label]) => html`<uui-checkbox
                label=${term(label)}
                ?checked=${this._filter.marks?.includes(mark) ?? false}
                @change=${(e: UUIBooleanInputEvent) => this.#toggleMark(mark, e.target.checked)}>${term(label)}</uui-checkbox>`,
            )}
          </div>`}
    `;
  }

  static override styles = css`
    :host {
      display: flex;
      flex-wrap: wrap;
      gap: var(--uui-size-space-3);
      align-items: center;
      margin-bottom: var(--uui-size-space-5);
    }

    .marks {
      display: flex;
      flex-wrap: wrap;
      gap: var(--uui-size-space-4);
    }
  `;
}

declare global {
  interface HTMLElementTagNameMap {
    "umbricians-feature-flags-filters": FeatureFlagsFiltersElement;
  }
}
