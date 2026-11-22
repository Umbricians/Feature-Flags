import { test } from "node:test";
import assert from "node:assert/strict";
import { addableConditions, displayNameOf, findByName, hasEditor, isRoot, namesMatch } from "./condition-names.ts";

const registered = [
  { name: "Microsoft.Targeting", displayName: "Audience" },
  { name: "Contoso.Culture", displayName: "Culture" },
  { name: "Umbricians.Root", displayName: "Site" },
];

test("names match ignoring case, and a name without a namespace matches the last segment", () => {
  assert.equal(namesMatch("Contoso.Culture", "contoso.culture"), true);
  assert.equal(namesMatch("Contoso.Culture", "Culture"), true);
  assert.equal(namesMatch("Microsoft.Targeting", "targeting"), true);
  assert.equal(namesMatch("Contoso.Product.Culture", "Product.Culture"), false);
  assert.equal(namesMatch("Contoso.Culture", "CultureFilter"), false);
});

test("an exact name wins over a short one", () => {
  assert.equal(findByName(["A.Culture", "Culture"], (n) => n, "culture"), "Culture");
});

test("Root is Root by either name", () => {
  assert.equal(isRoot("umbricians.root"), true);
  assert.equal(isRoot("Root"), true);
  assert.equal(isRoot("Contoso.Culture"), false);
});

test("a condition is shown by its registered display name, or by its name when unregistered", () => {
  assert.equal(displayNameOf(registered, { name: "culture", parameters: {} }), "Culture");
  assert.equal(displayNameOf(registered, { name: "Other.Thing", parameters: {} }), "Other.Thing");
});

test("an editor exists when a manifest's name matches", () => {
  assert.equal(hasEditor(["Contoso.Culture"], "Culture"), true);
  assert.equal(hasEditor(["Contoso.Culture"], "Targeting"), false);
});

test("the Add menu offers registered conditions that have an editor and aren't added yet", () => {
  const added = [{ name: "targeting", parameters: {} }];
  assert.deepEqual(
    addableConditions(registered, ["Microsoft.Targeting", "Contoso.Culture"], added).map((c) => c.name),
    ["Contoso.Culture"],
  );
});
