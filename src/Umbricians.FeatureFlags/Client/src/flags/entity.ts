import { FEATURE_FLAG_ENTITY_TYPE } from "../constants.js";

/**
 * What Umbraco's entity actions are given for a flag: the flag entity type and the flag's name as its unique. A
 * developer's entity action for `FEATURE_FLAG_ENTITY_TYPE` receives exactly this, in the workspace and on list rows.
 */
export function flagEntity(name: string): { entityType: string; unique: string } {
  return { entityType: FEATURE_FLAG_ENTITY_TYPE, unique: name };
}
