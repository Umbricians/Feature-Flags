import { test } from "node:test";
import assert from "node:assert/strict";
import { readRoots, rootRows, withRoot } from "./root.ts";
import type { SiteRoot } from "../../api/types.ts";

const uk: SiteRoot = { key: "aaa", name: "UK shop", domains: ["uk.example.com"], deleted: false };
const us: SiteRoot = { key: "bbb", name: "US shop", domains: [], deleted: false };

test("roots are read from Roots in any case, ignoring what isn't a key", () => {
  assert.deepEqual(readRoots({ Roots: ["a", 1, "b"] }), ["a", "b"]);
  assert.deepEqual(readRoots({ roots: ["a"] }), ["a"]);
  assert.deepEqual(readRoots({ Roots: "a" }), []);
  assert.deepEqual(readRoots({}), []);
});

test("choosing a root adds it once, and unchoosing removes it", () => {
  const one = withRoot({}, "aaa", true);
  assert.deepEqual(one, { Roots: ["aaa"] });
  assert.deepEqual(withRoot(one, "AAA", true), { Roots: ["aaa"] });
  assert.deepEqual(withRoot(withRoot(one, "bbb", true), "aaa", false), { Roots: ["bbb"] });
});

test("every root is a row, the chosen ones marked, with its domains", () => {
  const rows = rootRows(["bbb"], [uk, us]);
  assert.deepEqual(rows.map((row) => [row.name, row.selected, row.deleted]), [["UK shop", false, false], ["US shop", true, false]]);
  assert.deepEqual(rows[0]?.domains, ["uk.example.com"]);
});

test("a chosen key no root has any more is a deleted site with no name, which can be removed", () => {
  const rows = rootRows(["bbb", "gone"], [uk, us]);
  const deleted = rows[2];
  assert.deepEqual([deleted?.key, deleted?.name, deleted?.deleted, deleted?.selected], ["gone", null, true, true]);
  assert.deepEqual(withRoot({ Roots: ["bbb", "gone"] }, "gone", false), { Roots: ["bbb"] });
});

test("while the roots are unknown, no chosen key is called deleted", () => {
  const rows = rootRows(["aaa"], null);
  assert.deepEqual(rows.map((row) => [row.key, row.deleted, row.selected]), [["aaa", false, true]]);
});

test("choosing a root keeps the condition's other parameters and replaces Roots in any case", () => {
  assert.deepEqual(withRoot({ roots: ["aaa"], Other: 1 }, "bbb", true), { Other: 1, Roots: ["aaa", "bbb"] });
});
