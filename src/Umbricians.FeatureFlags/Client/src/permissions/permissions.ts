import type { UmbClassInterface } from "@umbraco-cms/backoffice/class-api";
import { UMB_CURRENT_USER_CONTEXT } from "@umbraco-cms/backoffice/current-user";

/** Calls back with whether one of the current user's groups grants the permission, and again whenever that changes. */
export function observePermission(host: UmbClassInterface, permission: string, callback: (permitted: boolean) => void) {
  host.consumeContext(UMB_CURRENT_USER_CONTEXT, (context) => {
    host.observe(
      context?.fallbackPermissions,
      (permissions) => callback(permissions?.includes(permission) ?? false),
      `umbriciansFeatureFlagsPermission_${permission}`,
    );
  });
}
