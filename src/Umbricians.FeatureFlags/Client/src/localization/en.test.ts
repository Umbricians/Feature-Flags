import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import en from "./en.ts";
import { FEATURE_FLAGS_PERMISSION_ENTITY_TYPE } from "../permissions/constants.ts";

const terms = en.umbriciansFeatureFlags as Record<string, string | ((...args: Array<any>) => string)>;

const srcDirectory = fileURLToPath(new URL("..", import.meta.url));
const source = readdirSync(srcDirectory, { recursive: true, encoding: "utf8" })
  .map((path) => path.replace(/\\/g, "/"))
  .filter((path) => path.endsWith(".ts") && !path.endsWith(".test.ts") && path !== "localization/en.ts")
  .map((path) => readFileSync(new URL(path, new URL("..", import.meta.url)), "utf8"))
  .join("\n");
const used = new Set([...source.matchAll(/umbriciansFeatureFlags_(\w+)/g)].map((match) => match[1]));

test("every term the source uses is in the English dictionary", () => {
  assert.ok(used.size > 0);
  const missing = [...used].filter((key) => !(key in terms));
  assert.deepEqual(missing, []);
});

test("every term in the English dictionary is used", () => {
  const unused = Object.keys(terms).filter((key) => !used.has(key));
  assert.deepEqual(unused, []);
});

test("the permission box is headed by the term for the entity type", () => {
  assert.ok(`permissionsEntityGroup_${FEATURE_FLAGS_PERMISSION_ENTITY_TYPE}` in en.user);
});

test("counted terms read right for one and for more", () => {
  const term = (key: string) => terms[key] as (count: number) => string;
  assert.equal(term("members")(1), "1 member");
  assert.equal(term("members")(3), "3 members");
  assert.equal(term("memberGroups")(1), "1 member group");
  assert.equal(term("otherGroups")(2), "2 other groups");
});
