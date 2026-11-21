import { UMB_WORKSPACE_PATH_PATTERN } from "@umbraco-cms/backoffice/workspace";
import { FEATURE_FLAG_ENTITY_TYPE, FEATURE_FLAGS_ROOT_ENTITY_TYPE } from "../constants.js";

/** The flag list in Settings. */
export const flagListPath = () =>
  UMB_WORKSPACE_PATH_PATTERN.generateAbsolute({ sectionName: "settings", entityType: FEATURE_FLAGS_ROOT_ENTITY_TYPE });

/** A flag's editor. The name is encoded, since flag names may hold slashes and spaces. */
export const editFlagPath = (name: string) =>
  `${UMB_WORKSPACE_PATH_PATTERN.generateAbsolute({ sectionName: "settings", entityType: FEATURE_FLAG_ENTITY_TYPE })}/edit/${encodeURIComponent(name)}`;
