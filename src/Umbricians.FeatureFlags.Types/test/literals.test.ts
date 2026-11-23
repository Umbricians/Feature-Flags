import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import * as client from "../../Umbricians.FeatureFlags/Client/src/constants.ts";
import * as permissions from "../../Umbricians.FeatureFlags/Client/src/permissions/constants.ts";

// `export type Name = "value";` lines of index.d.ts, as name to value.
const declared = new Map(
  [...readFileSync(new URL("../index.d.ts", import.meta.url), "utf8").matchAll(/export type (\w+) = "([^"]*)";/g)].map(
    (match) => [match[1], match[2]],
  ),
);
const clientNames: Record<string, string> = {
  "FeatureFlagsRootEntityType": "FEATURE_FLAGS_ROOT_ENTITY_TYPE",
  "FeatureFlagEntityType": "FEATURE_FLAG_ENTITY_TYPE",
  "FeatureFlagsRootWorkspaceAlias": "FEATURE_FLAGS_ROOT_WORKSPACE_ALIAS",
  "FeatureFlagWorkspaceAlias": "FEATURE_FLAG_WORKSPACE_ALIAS",
  "FeatureFlagsCollectionAlias": "FEATURE_FLAGS_COLLECTION_ALIAS",
  "FeatureFlagsMenuItemAlias": "FEATURE_FLAGS_MENU_ITEM_ALIAS",
  "FeatureFlagsConditionManifestType": "FEATURE_FLAGS_CONDITION_MANIFEST_TYPE",
  "FeatureFlagWorkspaceContextAlias": "FEATURE_FLAG_WORKSPACE_CONTEXT_ALIAS",
  "FeatureFlagsPermissionConditionAlias": "FEATURE_FLAGS_PERMISSION_CONDITION_ALIAS",
  "FeatureFlagsPermissionView": "FEATURE_FLAGS_PERMISSION_VIEW",
  "FeatureFlagsPermissionManage": "FEATURE_FLAGS_PERMISSION_MANAGE"
};
const actual: Record<string, unknown> = { ...client, ...permissions };

test("every string the types package declares is the string the client uses", () => {
  assert.ok(declared.size >= 11, "the declarations weren't found");
  for (const [name, value] of declared) {
    assert.equal(actual[clientNames[name]], value, `${name} differs from the client's constants`);
  }
});

test("every promised string is declared by the types package", () => {
  const promised = [
    "FeatureFlagsRootEntityType",
    "FeatureFlagEntityType",
    "FeatureFlagsRootWorkspaceAlias",
    "FeatureFlagWorkspaceAlias",
    "FeatureFlagsCollectionAlias",
    "FeatureFlagsMenuItemAlias",
    "FeatureFlagsConditionManifestType",
    "FeatureFlagWorkspaceContextAlias",
    "FeatureFlagsPermissionConditionAlias",
    "FeatureFlagsPermissionView",
    "FeatureFlagsPermissionManage",
  ];
  assert.deepEqual(promised.filter((name) => !declared.has(name)), []);
});
