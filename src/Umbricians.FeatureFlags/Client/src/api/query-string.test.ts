import { test } from "node:test";
import assert from "node:assert/strict";
import { flagsQueryString } from "./query-string.ts";

test("sends skip and take by default", () => {
  assert.equal(flagsQueryString({}), "skip=0&take=25");
  assert.equal(flagsQueryString({ skip: 50, take: 10 }), "skip=50&take=10");
});

test("leaves out empty filters", () => {
  assert.equal(flagsQueryString({ filter: "", state: "", tag: "", marks: [] }), "skip=0&take=25");
  assert.equal(
    flagsQueryString({ filter: "new", state: "off", show: "undeclared", tag: "beta", orderBy: "updated" }),
    "skip=0&take=25&filter=new&state=off&show=undeclared&tag=beta&orderBy=updated",
  );
});

test("joins marks with commas", () => {
  assert.equal(flagsQueryString({ marks: ["changed", "twice"] }), "skip=0&take=25&marks=changed%2Ctwice");
});

test("sends an empty section, which asks for flags in no section", () => {
  assert.equal(flagsQueryString({ section: "" }), "skip=0&take=25&section=");
});

test("leaves out a null or missing section", () => {
  assert.equal(flagsQueryString({ section: null }), "skip=0&take=25");
  assert.equal(flagsQueryString({}), "skip=0&take=25");
});

test("encodes names with dots and spaces", () => {
  assert.equal(flagsQueryString({ section: "Check out.&more", filter: "a b" }), "skip=0&take=25&filter=a+b&section=Check+out.%26more");
});
