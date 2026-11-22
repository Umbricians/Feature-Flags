import { UmbRepositoryBase } from "@umbraco-cms/backoffice/repository";
import type { UmbCollectionFilterModel, UmbCollectionRepository } from "@umbraco-cms/backoffice/collection";
import { fetchFlags, type FlagQuery } from "../../api/feature-flags.resource.js";
import { flagEntity } from "../entity.js";
import type { FeatureFlagCollectionItem } from "../display.js";

/** The collection's filter: Umbraco's paging and search, plus the flag list's own filters. */
export type FeatureFlagsCollectionFilter = UmbCollectionFilterModel & Omit<FlagQuery, "skip" | "take" | "filter">;

/** Reads a page of flags from the Feature Flags API; the server searches, filters and sorts across every flag. */
export class FeatureFlagsCollectionRepository
  extends UmbRepositoryBase
  implements UmbCollectionRepository<FeatureFlagCollectionItem, FeatureFlagsCollectionFilter>
{
  async requestCollection(filter: FeatureFlagsCollectionFilter) {
    try {
      // In section order by default, so each of the list's sections appears once.
      const page = await fetchFlags({ ...filter, orderBy: filter.orderBy ?? "section" });
      const items: Array<FeatureFlagCollectionItem> = page.items.map((flag) => ({
        ...flag,
        ...flagEntity(flag.name),
      }));
      return { data: { items, total: page.total } };
    } catch (error) {
      return { error: error as Error };
    }
  }
}

export { FeatureFlagsCollectionRepository as api };
