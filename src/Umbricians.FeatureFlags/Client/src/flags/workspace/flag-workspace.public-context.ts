import { UmbContextBase } from "@umbraco-cms/backoffice/class-api";
import { UmbContextToken } from "@umbraco-cms/backoffice/context-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import type { Observable } from "@umbraco-cms/backoffice/external/rxjs";
import type { FeatureFlag } from "../../api/types.js";
import { FEATURE_FLAG_WORKSPACE_CONTEXT_ALIAS } from "../../constants.js";

/**
 * The flag workspace's read-only context, which developers' own workspace views and entity actions can use. It shows
 * the flag as the API returns it and offers no way to change it: changes go through conditions or the Management API.
 */
export interface UmbriciansFeatureFlagWorkspaceContext {
  /** The flag being edited, as saved: undefined until it has loaded. */
  readonly flag: Observable<FeatureFlag | undefined>;
}

/** The token for the read-only context: `new UmbContextToken("Umbricians.FeatureFlags.FlagWorkspaceContext.Public")` is equivalent. */
export const UMBRICIANS_FEATURE_FLAG_WORKSPACE_CONTEXT = new UmbContextToken<FeatureFlagPublicWorkspaceContext>(
  FEATURE_FLAG_WORKSPACE_CONTEXT_ALIAS,
);

/** Provides the read-only facade from inside the flag workspace; the editing context stays internal. */
export class FeatureFlagPublicWorkspaceContext extends UmbContextBase implements UmbriciansFeatureFlagWorkspaceContext {
  readonly flag: Observable<FeatureFlag | undefined>;

  constructor(host: UmbControllerHost, flag: Observable<FeatureFlag | undefined>) {
    super(host, UMBRICIANS_FEATURE_FLAG_WORKSPACE_CONTEXT);
    this.flag = flag;
  }
}
