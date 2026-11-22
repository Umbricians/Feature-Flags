import { test } from "node:test";
import assert from "node:assert/strict";
import type { ManifestUmbriciansFeatureFlagsCondition } from "./conditions/condition.extension.ts";
import { manifests } from "./bundle.manifests.ts";

test("every extension alias carries the package id", () => {
  const strays = manifests.map((manifest) => manifest.alias).filter((alias) => !alias.startsWith("Umbricians.FeatureFlags."));
  assert.deepEqual(strays, []);
});

test("no two extensions share an alias", () => {
  const aliases = manifests.map((manifest) => manifest.alias);
  assert.equal(new Set(aliases).size, aliases.length);
});

test("the audience is the package's own condition editor, found by the C# condition's name", () => {
  const conditions = manifests.filter(
    (manifest): manifest is ManifestUmbriciansFeatureFlagsCondition => manifest.type === "umbriciansFeatureFlagsCondition",
  );
  assert.deepEqual(
    conditions.map((manifest) => [manifest.alias, manifest.meta.name]),
    [["Umbricians.FeatureFlags.Condition.Audience", "Microsoft.Targeting"]],
  );
});
