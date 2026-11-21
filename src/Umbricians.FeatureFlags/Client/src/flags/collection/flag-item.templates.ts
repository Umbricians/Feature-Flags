import { html, nothing } from "@umbraco-cms/backoffice/external/lit";
import type { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";
import { shortName, stateColor, type FeatureFlagCollectionItem } from "../display.js";
import { describeAudience, markers, stateLabel } from "../describe.js";
import { editFlagPath } from "../paths.js";
import type { FeatureFlagsCollectionFilter } from "./flags-collection.repository.js";

// The parts of a flag the table and the cards both show, written once so they stay the same.

/** The flag's name, linking to its editor; without its section in front while the list is in sections. */
export function flagName(item: FeatureFlagCollectionItem, orderBy: FeatureFlagsCollectionFilter["orderBy"]) {
  return item.isDeclared
    ? html`<a href=${editFlagPath(item.name)} title=${item.name}>${orderBy === "updated" ? item.name : shortName(item)}</a>`
    : html`<strong title=${item.name}>${item.name}</strong>`;
}

/** The flag's state as a tag. */
export function stateTag(localize: UmbLocalizationController, item: FeatureFlagCollectionItem) {
  return html`<uui-tag color=${stateColor(item.state)} look=${item.state === "off" ? "secondary" : "primary"}>${stateLabel(localize, item.state)}</uui-tag>`;
}

/** Who the flag is on for, in words, or who it keeps while it is off. */
export function audienceNote(localize: UmbLocalizationController, item: FeatureFlagCollectionItem, cssClass = "") {
  if (!item.audience) return nothing;
  const audience = describeAudience(localize, item.audience);
  if (!audience) return nothing;
  return html`<small class=${cssClass}>${item.state === "off" ? localize.term("umbriciansFeatureFlags_keeps", audience) : audience}</small>`;
}

/** The flag's notes, such as Changed here, as tags. */
export function markerTags(localize: UmbLocalizationController, item: FeatureFlagCollectionItem) {
  return markers(localize, item).map((marker) => html`<uui-tag color=${marker.color} look="outline">${marker.label}</uui-tag>`);
}
