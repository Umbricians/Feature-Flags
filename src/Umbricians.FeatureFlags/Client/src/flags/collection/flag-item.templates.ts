import { html, nothing } from "@umbraco-cms/backoffice/external/lit";
import type { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";
import { listNote, nameParts, shortName, stateColor, type FeatureFlagCollectionItem } from "../display.js";
import { describeAudience, markers, stateLabel } from "../describe.js";
import { editFlagPath } from "../paths.js";
import type { FeatureFlagsCollectionFilter } from "./flags-collection.repository.js";

// The parts of a flag the table and the cards both show, written once so they stay the same.

/** The flag's name, linking to its editor; without its section in front while the list is in sections. */
export function flagName(item: FeatureFlagCollectionItem, orderBy: FeatureFlagsCollectionFilter["orderBy"]) {
  const text = wrappable(item.isDeclared && orderBy !== "updated" ? shortName(item) : item.name);
  return item.isDeclared
    ? html`<a href=${editFlagPath(item.name)} title=${item.name}>${text}</a>`
    : html`<strong title=${item.name}>${text}</strong>`;
}

/** A name with a line-break opportunity between its words, so a long one wraps where a reader would split it. */
function wrappable(name: string) {
  return nameParts(name).map((part, index) => (index === 0 ? part : html`<wbr>${part}`));
}

/** The flag's state as a tag. */
export function stateTag(localize: UmbLocalizationController, item: FeatureFlagCollectionItem) {
  return html`<uui-tag color=${stateColor(item.state)} look=${item.state === "off" ? "secondary" : "primary"}>${stateLabel(localize, item.state)}</uui-tag>`;
}

/** Who the flag is on for, in words (or what its conditions amount to), or what it keeps while it is off. */
export function audienceNote(localize: UmbLocalizationController, item: FeatureFlagCollectionItem, cssClass = "") {
  const note = listNote(item);
  if (!note) return nothing;
  const text = note.kind === "summary" ? note.text : describeAudience(localize, item.audience);
  return html`<small class=${cssClass}>${item.state === "off" ? localize.term("umbriciansFeatureFlags_keeps", text) : text}</small>`;
}

/** The flag's notes, such as Changed here, as tags. */
export function markerTags(localize: UmbLocalizationController, item: FeatureFlagCollectionItem) {
  return markers(localize, item).map((marker) => html`<uui-tag color=${marker.color} look="outline">${marker.label}</uui-tag>`);
}
