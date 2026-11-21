import type { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";
import type { AudienceCounts, FeatureFlagListItem } from "../api/types.js";
import { audienceParts, joinList } from "./display.js";

/** Who a flag is (or will be) on for, in words: "everyone", or "3 members and 1 member group". */
export function describeAudience(localize: UmbLocalizationController, counts: AudienceCounts | null, hasAudience = counts !== null): string {
  if (!hasAudience || counts === null) return localize.term("umbriciansFeatureFlags_everyone");
  const parts = audienceParts(counts).map((part) =>
    part.kind === "members"
      ? localize.term("umbriciansFeatureFlags_members", part.count)
      : part.kind === "memberGroups"
        ? localize.term("umbriciansFeatureFlags_memberGroups", part.count)
        : localize.term("umbriciansFeatureFlags_otherGroups", part.count),
  );
  return parts.length ? joinList(parts, localize.term("umbriciansFeatureFlags_and")) : localize.term("umbriciansFeatureFlags_nobody");
}

/** The state's label, as the list and editor show it. */
export function stateLabel(localize: UmbLocalizationController, state: FeatureFlagListItem["state"]): string {
  switch (state) {
    case "off":
      return localize.term("umbriciansFeatureFlags_stateOff");
    case "everyone":
      return localize.term("umbriciansFeatureFlags_stateEveryone");
    case "audience":
      return localize.term("umbriciansFeatureFlags_stateAudience");
    default:
      return localize.term("umbriciansFeatureFlags_stateCustom");
  }
}

/** The small markers beside a flag: changed here, differs from default, declared twice. */
export function markers(localize: UmbLocalizationController, item: FeatureFlagListItem): Array<{ label: string; color: "default" | "warning" | "danger" }> {
  const result: Array<{ label: string; color: "default" | "warning" | "danger" }> = [];
  if (item.hasOverride && item.isDeclared) result.push({ label: localize.term("umbriciansFeatureFlags_changedHere"), color: "default" });
  if (item.differsFromDefault) result.push({ label: localize.term("umbriciansFeatureFlags_differs"), color: "warning" });
  if (item.declaredTwice) result.push({ label: localize.term("umbriciansFeatureFlags_declaredTwice"), color: "danger" });
  return result;
}

/** Who last changed the flag and when, or that it uses its default. */
export function changedText(localize: UmbLocalizationController, item: FeatureFlagListItem): string {
  if (!item.updatedUtc) return localize.term("umbriciansFeatureFlags_usesDefault");
  const when = localize.date(item.updatedUtc, { dateStyle: "medium", timeStyle: "short" });
  return item.updatedBy?.name ? localize.term("umbriciansFeatureFlags_changedBy", item.updatedBy.name, when) : when;
}
