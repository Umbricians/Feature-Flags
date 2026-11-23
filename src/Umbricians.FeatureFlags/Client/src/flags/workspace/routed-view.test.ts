import { test } from "node:test";
import assert from "node:assert/strict";
import { routedViewAlias } from "./routed-view.ts";

const views = [
  { alias: "Settings.View", meta: { pathname: "settings" } },
  { alias: "Definition.View", meta: { pathname: "definition" } },
  { alias: "About.View", meta: { pathname: "about" } },
];
const base = "/umbraco/section/settings/workspace/umbricians-feature-flags-flag/edit";

test("no view segment is the first view", () => {
  assert.equal(routedViewAlias(views, `${base}/Checkout`), "Settings.View");
  assert.equal(routedViewAlias(views, `${base}/Checkout/`), "Settings.View");
});

test("a view segment picks that view", () => {
  assert.equal(routedViewAlias(views, `${base}/Checkout/view/settings`), "Settings.View");
  assert.equal(routedViewAlias(views, `${base}/Checkout/view/about`), "About.View");
  assert.equal(routedViewAlias(views, `${base}/Checkout/view/about?x=1#y`), "About.View");
});

test("an unknown view is null", () => {
  assert.equal(routedViewAlias(views, `${base}/Checkout/view/nope`), null);
});

test("no views is null", () => {
  assert.equal(routedViewAlias([], `${base}/Checkout`), null);
  assert.equal(routedViewAlias([], ""), null);
});

test("an empty path is the first view", () => {
  assert.equal(routedViewAlias(views, ""), "Settings.View");
});

test("an encoded flag name that looks like a view path is not read as one", () => {
  const name = encodeURIComponent("a/view/about");
  assert.equal(routedViewAlias(views, `${base}/${name}`), "Settings.View");
  assert.equal(routedViewAlias(views, `${base}/${name}/view/definition`), "Definition.View");
});
