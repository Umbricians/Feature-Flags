import { css, customElement, html, nothing, repeat, state } from "@umbraco-cms/backoffice/external/lit";
import { UmbCollectionViewElementBase, UMB_COLLECTION_CONTEXT } from "@umbraco-cms/backoffice/collection";
import { UmbTextStyles } from "@umbraco-cms/backoffice/style";
import { sections, stateFill, type FeatureFlagCollectionItem } from "../display.js";
import { changedText, markers, stateLabel } from "../describe.js";
import { observeFlagEntityActions } from "../entity-actions.js";
import { audienceNote, flagName } from "./flag-item.templates.js";
import type { FeatureFlagsCollectionFilter } from "./flags-collection.repository.js";
import "./flags-filters.element.js";
import "./flag-switch.element.js";
import "./flag-row-actions.element.js";

/**
 * The flag list as cards, in its sections. Each card puts the flag's state at the top, as a circle and in words; the
 * name and what it is for in the middle; and the switch with the last change in a footer pinned to the card's bottom,
 * so switches line up across a row. Each line holds at most one piece of text that can be long, so none of them has
 * to share its width with another.
 */
@customElement("umbricians-feature-flags-card-view")
export class FeatureFlagsCardViewElement extends UmbCollectionViewElementBase<FeatureFlagCollectionItem> {
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
      ${sections(this._items, this._orderBy).map(
        (section) => html`
          ${this._orderBy === "updated" ? nothing : html`<h3>${section.name ?? this.localize.term("umbriciansFeatureFlags_noSection")}</h3>`}
          <div class="grid">${repeat(section.items, (item) => item.unique, (item) => this.#renderCard(item))}</div>
        `,
      )}
    `;
  }

  #renderCard(item: FeatureFlagCollectionItem) {
    const notes = markers(this.localize, item);
    return html`<article class="card state-${item.state}">
      <div class="state">
        <span class="circle ${stateFill(item.state)}" aria-hidden="true"></span>
        <div class="state-text">
          <strong>${stateLabel(this.localize, item.state)}</strong>
          ${audienceNote(this.localize, item)}
        </div>
      </div>
      <div class="name">
        ${flagName(item, this._orderBy)}
        ${this._hasEntityActions ? html`<umbricians-feature-flags-row-actions .name=${item.unique}></umbricians-feature-flags-row-actions>` : nothing}
        ${item.description ? html`<small class="description">${item.description}</small>` : nothing}
      </div>
      ${notes.length ? html`<div class="markers">${notes.map((marker) => html`<span class=${marker.color}>${marker.label}</span>`)}</div>` : nothing}
      ${item.tags.length
        ? html`<small class="tags"><umb-icon name="icon-tag"></umb-icon><span>${item.tags.join(", ")}</span></small>`
        : nothing}
      <div class="footer">
        <small>${changedText(this.localize, item)}</small>
        <umbricians-feature-flags-switch .item=${item}></umbricians-feature-flags-switch>
      </div>
    </article>`;
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
        grid-template-columns: repeat(auto-fill, minmax(min(100%, 300px), 1fr));
        gap: var(--uui-size-space-4);
      }
      /* Looks like a uui-box, but is a plain element so the footer can be pinned to the bottom of a stretched card. */
      .card {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-3);
        min-width: 0;
        padding: var(--uui-size-space-4);
        background: var(--uui-color-surface);
        border-radius: var(--uui-border-radius);
        box-shadow: var(--uui-shadow-depth-1);
      }

      /* The state: the circle's fill says how much is on, its colour what kind. */
      .card {
        --state-color: var(--uui-color-border-emphasis);
        --state-text: var(--uui-color-text-alt);
      }
      .card.state-everyone {
        --state-color: var(--uui-color-positive);
        --state-text: var(--uui-color-positive-standalone);
      }
      .card.state-audience,
      .card.state-conditions {
        --state-color: var(--uui-color-default);
        --state-text: var(--uui-color-default-standalone);
      }
      .card.state-custom {
        --state-color: var(--uui-color-warning);
        --state-text: var(--uui-color-warning-standalone);
      }
      .state {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-3);
        padding-bottom: var(--uui-size-space-3);
        border-bottom: 1px solid var(--uui-color-divider);
      }
      .circle {
        flex: none;
        box-sizing: border-box;
        width: 22px;
        height: 22px;
        border-radius: 50%;
        border: 3px solid var(--state-color);
      }
      .circle.half {
        background: linear-gradient(90deg, var(--state-color) 50%, transparent 50%);
      }
      .circle.full {
        background: var(--state-color);
      }
      .state-text {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
        min-width: 0;
      }
      .state-text strong {
        color: var(--state-text);
      }

      /* The name shares its line only with the actions button; the description below it takes the full width. */
      .name {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        align-items: start;
        gap: var(--uui-size-space-1) var(--uui-size-space-2);
        overflow-wrap: anywhere;
      }
      .name a,
      .name strong {
        font-size: var(--uui-type-default-size, 14px);
        font-weight: bold;
      }
      .name .description {
        grid-column: 1 / -1;
      }

      /* Notes and tags as text, not tags: a card would otherwise be mostly pills. */
      .markers {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-1) var(--uui-size-space-4);
        font-size: var(--uui-type-small-size, 12px);
        font-weight: bold;
        color: var(--uui-color-default-standalone);
      }
      .markers span {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }
      .markers span::before {
        content: "";
        width: 6px;
        height: 6px;
        border-radius: 50%;
        background: currentColor;
      }
      .markers .warning {
        color: var(--uui-color-warning-standalone);
      }
      .markers .danger {
        color: var(--uui-color-danger-standalone);
      }
      .tags {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-2);
        overflow-wrap: anywhere;
      }
      .tags umb-icon {
        flex: none;
        margin-top: 2px;
      }

      .footer {
        margin-top: auto;
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding-top: var(--uui-size-space-3);
        border-top: 1px solid var(--uui-color-divider);
      }
      .footer small {
        margin-right: auto;
        min-width: 0;
      }
      small {
        color: var(--uui-color-text-alt);
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
