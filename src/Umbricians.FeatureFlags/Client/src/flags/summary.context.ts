import { UmbContextBase } from "@umbraco-cms/backoffice/class-api";
import { UmbContextToken } from "@umbraco-cms/backoffice/context-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UmbObjectState } from "@umbraco-cms/backoffice/observable-api";
import { fetchSummary, type FeatureFlagSummary } from "../api/feature-flags.resource.js";

/**
 * The sections, tags and site details the flag list shows, shared by its header, filters and views. Read again after
 * any change, since a change can move a flag in or out of the undeclared list.
 */
export class FeatureFlagsSummaryContext extends UmbContextBase {
  #summary = new UmbObjectState<FeatureFlagSummary | undefined>(undefined);
  readonly summary = this.#summary.asObservable();

  constructor(host: UmbControllerHost) {
    super(host, FEATURE_FLAGS_SUMMARY_CONTEXT);
    this.load();
  }

  async load() {
    try {
      this.#summary.setValue(await fetchSummary());
    } catch {
      // The list shows its own error; without a summary the filters simply offer no sections or tags.
    }
  }
}

export const FEATURE_FLAGS_SUMMARY_CONTEXT = new UmbContextToken<FeatureFlagsSummaryContext>("UmbriciansFeatureFlagsSummaryContext");
