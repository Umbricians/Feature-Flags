import { test } from "node:test";
import assert from "node:assert/strict";
import { audienceParts, joinList, sections, shortName, switchKind } from "./display.ts";

test("the group is taken off the front of the name", () => {
  assert.equal(shortName({ name: "Checkout.NewFlow", group: "Checkout" }), "NewFlow");
  assert.equal(shortName({ name: "checkout.NewFlow", group: "Checkout" }), "NewFlow");
  assert.equal(shortName({ name: "GiftCards", group: "Payments" }), "GiftCards");
  assert.equal(shortName({ name: "DarkMode", group: null }), "DarkMode");
  assert.equal(shortName({ name: "Checkout.", group: "Checkout" }), "Checkout.");
});

test("a page is split into sections by group, in order", () => {
  const items = [{ group: "A" }, { group: "A" }, { group: "B" }, { group: null }];
  assert.deepEqual(
    sections(items, "name").map((s) => [s.name, s.items.length]),
    [["A", 2], ["B", 1], [null, 1]],
  );
});

test("sorted by last change, a page stays in one section", () => {
  assert.equal(sections([{ group: "A" }, { group: "B" }], "updated").length, 1);
  assert.equal(sections([], "updated").length, 0);
});

test("only editors who can manage flags get a switch, and undeclared flags get removed instead", () => {
  assert.equal(switchKind({ isDeclared: true, editable: true }, true), "switch");
  assert.equal(switchKind({ isDeclared: true, editable: true }, false), "readOnly");
  assert.equal(switchKind({ isDeclared: true, editable: false }, true), "readOnly");
  assert.equal(switchKind({ isDeclared: false, editable: false }, true), "remove");
  assert.equal(switchKind({ isDeclared: false, editable: false }, false), "readOnly");
});

test("an audience lists only the parts with anyone in them", () => {
  assert.deepEqual(audienceParts({ members: 3, memberGroups: 0, groups: 1 }), [
    { kind: "members", count: 3 },
    { kind: "groups", count: 1 },
  ]);
  assert.deepEqual(audienceParts(null), []);
});

test("lists read as prose", () => {
  assert.equal(joinList([], "and"), "");
  assert.equal(joinList(["a"], "and"), "a");
  assert.equal(joinList(["a", "b"], "and"), "a and b");
  assert.equal(joinList(["a", "b", "c"], "and"), "a, b and c");
});
