import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import * as client from "../../Umbricians.FeatureFlags/Client/src/constants.ts";
import * as permissions from "../../Umbricians.FeatureFlags/Client/src/permissions/constants.ts";

// `export declare const NAME: "value";` lines of index.d.ts, as name to value.
const declared = new Map(
  [...readFileSync(new URL("../index.d.ts", import.meta.url), "utf8").matchAll(/export declare const (\w+): "([^"]*)";/g)].map(
    (match) => [match[1], match[2]],
  ),
);
const actual: Record<string, unknown> = { ...client, ...permissions };

test("every string the types package declares is the string the client uses", () => {
  assert.ok(declared.size >= 11, "the declarations weren't found");
  for (const [name, value] of declared) {
    assert.equal(actual[name], value, `${name} differs from the client's constants`);
  }
});

test("every promised string is declared by the types package", () => {
  const promised = [
    "FEATURE_FLAGS_ROOT_ENTITY_TYPE",
    "FEATURE_FLAG_ENTITY_TYPE",
    "FEATURE_FLAGS_ROOT_WORKSPACE_ALIAS",
    "FEATURE_FLAG_WORKSPACE_ALIAS",
    "FEATURE_FLAGS_COLLECTION_ALIAS",
    "FEATURE_FLAGS_MENU_ITEM_ALIAS",
    "FEATURE_FLAGS_CONDITION_MANIFEST_TYPE",
    "FEATURE_FLAG_WORKSPACE_CONTEXT_ALIAS",
    "FEATURE_FLAGS_PERMISSION_CONDITION_ALIAS",
    "FEATURE_FLAGS_PERMISSION_VIEW",
    "FEATURE_FLAGS_PERMISSION_MANAGE",
  ];
  assert.deepEqual(promised.filter((name) => !declared.has(name)), []);
});
