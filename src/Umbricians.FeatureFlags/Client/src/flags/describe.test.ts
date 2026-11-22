import { test } from "node:test";
import assert from "node:assert/strict";
import type { UmbLocalizationController } from "@umbraco-cms/backoffice/localization-api";
import en from "../localization/en.ts";
import { describeAudience } from "./describe.ts";
import { countAudience, emptyAudience } from "./audience.ts";
import type { AudienceGroup } from "../api/types.ts";

const terms = en.umbriciansFeatureFlags as Record<string, string | ((...args: Array<any>) => string)>;
const localize = {
  term: (key: string, ...args: Array<unknown>) => {
    const term = terms[key.replace("umbriciansFeatureFlags_", "")];
    return typeof term === "function" ? term(...args) : term;
  },
} as unknown as UmbLocalizationController;

const group = (id: string, percentage: number): AudienceGroup => ({ id, kind: "unknown", name: null, percentage });

test("a group below 100% reads as in part, and a group at 0% is not fully included", () => {
  const audience = { ...emptyAudience(), members: ["m"], groups: [group("a", 100), group("b", 0), group("c", 40)] };

  assert.equal(countAudience(audience).partialGroups, 2);
  assert.equal(describeAudience(localize, countAudience(audience)), "1 member and 3 other groups (2 in part)");
});

test("groups all in full have no part to mention", () => {
  const audience = { ...emptyAudience(), groups: [group("a", 100)] };

  assert.equal(describeAudience(localize, countAudience(audience)), "1 other group");
});
