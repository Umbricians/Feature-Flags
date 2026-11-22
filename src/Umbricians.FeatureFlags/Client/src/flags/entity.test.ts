import { test } from "node:test";
import assert from "node:assert/strict";
import { FEATURE_FLAG_ENTITY_TYPE } from "../constants.ts";
import { manifests } from "../bundle.manifests.ts";
import { flagEntity } from "./entity.ts";

// The test of Umbraco's own entity action lookup (entity-actions-bundle and entity-action-list): the actions shown are
// the entityAction manifests whose forEntityTypes includes the entity's type.
const shownFor = (entityType: string, actions: Array<{ forEntityTypes: Array<string> }>) =>
  actions.filter((action) => action.forEntityTypes.includes(entityType));

test("a flag's entity is the flag entity type with the flag's name as its unique", () => {
  assert.deepEqual(flagEntity("Checkout.ExpressPay"), { entityType: "umbricians-feature-flags-flag", unique: "Checkout.ExpressPay" });
});

test("an entity action registered for the flag entity type is found for a flag, in the workspace and on a row alike", () => {
  const developers = [{ forEntityTypes: [FEATURE_FLAG_ENTITY_TYPE] }, { forEntityTypes: ["document"] }];
  assert.equal(shownFor(flagEntity("Any").entityType, developers).length, 1);
});

test("the package registers no entity actions of its own, which would show beside a developer's", () => {
  assert.deepEqual(manifests.filter((manifest) => manifest.type === "entityAction"), []);
});
