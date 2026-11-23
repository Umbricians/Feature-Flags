import { UmbContextToken } from "@umbraco-cms/backoffice/context-api";
import type { UmbWorkspaceContext } from "@umbraco-cms/backoffice/workspace";
import { FEATURE_FLAG_ENTITY_TYPE } from "../../constants.js";
import type { FeatureFlagWorkspaceContext } from "./flag-workspace.context.js";

/** The flag workspace's context, for its editor and tabs: only matches inside a flag's workspace. */
export const FEATURE_FLAG_WORKSPACE_CONTEXT = new UmbContextToken<UmbWorkspaceContext, FeatureFlagWorkspaceContext>(
  "UmbWorkspaceContext",
  undefined,
  (context): context is FeatureFlagWorkspaceContext => context.getEntityType() === FEATURE_FLAG_ENTITY_TYPE,
);
