import { UmbContextBase } from "@umbraco-cms/backoffice/class-api";
import type { UmbControllerHost } from "@umbraco-cms/backoffice/controller-api";
import { UMB_WORKSPACE_CONTEXT, UmbWorkspaceRouteManager, type UmbRoutableWorkspaceContext } from "@umbraco-cms/backoffice/workspace";
import { FEATURE_FLAG_ENTITY_TYPE, FEATURE_FLAG_WORKSPACE_ALIAS } from "../../constants.js";
import { FeatureFlagEditorElement } from "./flag-editor.element.js";

/**
 * The flag editor's workspace, at …/edit/{name}. The editor loads and saves the flag itself; the workspace only routes
 * to it, since flags aren't Umbraco entities with a detail repository of their own.
 */
export class FeatureFlagWorkspaceContext extends UmbContextBase implements UmbRoutableWorkspaceContext {
  readonly workspaceAlias = FEATURE_FLAG_WORKSPACE_ALIAS;
  readonly routes = new UmbWorkspaceRouteManager(this);

  constructor(host: UmbControllerHost) {
    super(host, UMB_WORKSPACE_CONTEXT);
    this.routes.setRoutes([
      {
        path: "edit/:name",
        component: FeatureFlagEditorElement,
        setup: (component, info) => {
          (component as FeatureFlagEditorElement).flagName = decodeName(info.match.params.name);
        },
      },
    ]);
  }

  getEntityType() {
    return FEATURE_FLAG_ENTITY_TYPE;
  }
}

function decodeName(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    // Already decoded, and holds a % of its own.
    return value;
  }
}

export { FeatureFlagWorkspaceContext as api };
