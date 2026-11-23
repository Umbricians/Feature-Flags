import { test } from "node:test";
import assert from "node:assert/strict";
import { FeatureFlagsRequestError, describeFailure, readProblem } from "./request-error.ts";

test("readProblem reads the detail and the problems of a problem details body", () => {
  assert.deepEqual(
    readProblem({ title: "Flag can't be changed here", detail: "The conditions are invalid.", problems: ["Time window: choose a time."] }),
    { detail: "The conditions are invalid.", problems: ["Time window: choose a time."] },
  );
});

test("readProblem reads the problems when there is no detail, rather than the generic title", () => {
  assert.deepEqual(readProblem({ title: "Flag can't be changed here", problems: ["Root: choose a site."] }), {
    detail: undefined,
    problems: ["Root: choose a site."],
  });
});

test("readProblem falls back to the title when there is nothing else", () => {
  assert.equal(readProblem({ title: "Conflict" }).detail, "Conflict");
});

test("readProblem reads a body that arrived as JSON text, or as plain text", () => {
  assert.equal(readProblem('{"detail":"No."}').detail, "No.");
  assert.equal(readProblem("Gateway said no").detail, "Gateway said no");
});

test("readProblem does not repeat the detail among the problems", () => {
  assert.deepEqual(readProblem({ detail: "Choose a site.", problems: ["Choose a site.", "Other."] }).problems, ["Other."]);
});

test("readProblem reads nothing from an empty or unknown body", () => {
  assert.deepEqual(readProblem(undefined), { detail: undefined, problems: [] });
  assert.deepEqual(readProblem(42), { detail: undefined, problems: [] });
});

test("describeFailure gives the reason, then each problem", () => {
  assert.equal(describeFailure(new FeatureFlagsRequestError(409, "Not allowed.", ["One.", "Two."])), "Not allowed. One. Two.");
});

test("describeFailure gives only the problems when there is no reason", () => {
  assert.equal(describeFailure(new FeatureFlagsRequestError(409, undefined, ["One."])), "One.");
});

test("describeFailure falls back to the status when the server said nothing", () => {
  assert.equal(describeFailure(new FeatureFlagsRequestError(500, undefined)), "Feature Flags request failed (500)");
});

test("describeFailure words other errors as they are, without an Error: prefix", () => {
  assert.equal(describeFailure(new Error("boom")), "boom");
  assert.equal(describeFailure("text"), "text");
});
