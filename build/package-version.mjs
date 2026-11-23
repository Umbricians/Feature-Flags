// Prints the version a CI build packs for one Umbraco major, from GitHub's ref variables and the repository's tags.
//
//   node build/package-version.mjs <Umbraco major>
//
// The package's major is the Umbraco major it supports, and each major is built separately (-p:UmbracoMajor=18).
// A tag v18.2.3 builds 18.2.3, and fails for any other major. Any other build is {next patch}-ci.{run number}, where
// the next patch follows the highest stable release tag of that major, or is {major}.0.0 before its first release.
// Needs the tags fetched.
import { execFileSync } from "node:child_process";

const SEMANTIC_VERSION = /^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$/;

const [, , umbracoMajor] = process.argv;
const { GITHUB_REF_TYPE, GITHUB_REF_NAME = "", GITHUB_RUN_NUMBER = "0" } = process.env;

if (!/^[0-9]+$/.test(umbracoMajor ?? "")) {
  console.error("Usage: node package-version.mjs <Umbraco major>");
  process.exit(1);
}

const version = GITHUB_REF_TYPE === "tag"
  ? GITHUB_REF_NAME.replace(/^v/, "")
  : `${nextPatch() ?? `${umbracoMajor}.0.0`}-ci.${GITHUB_RUN_NUMBER}`;

if (!SEMANTIC_VERSION.test(version)) {
  console.error(`'${version}' is not a valid package version`);
  process.exit(1);
}
if (version.split(".")[0] !== umbracoMajor) {
  console.error(`'${version}' doesn't start with the Umbraco major ${umbracoMajor} this build is for`);
  process.exit(1);
}
console.log(version);

function nextPatch() {
  const lastRelease = lastStableTag();
  if (!lastRelease) return undefined;
  const [major, minor, patch] = lastRelease.slice(1).split(".").map(Number);
  return `${major}.${minor}.${patch + 1}`;
}

// The highest stable tag of this major in the repository, reachable or not: Gitflow tags releases on main before
// develop has them. Other majors' tags don't count.
function lastStableTag() {
  const tags = execFileSync("git", ["tag", "--list", `v${umbracoMajor}.*.*`, "--sort=-v:refname"], { encoding: "utf8" });
  return tags.split("\n").find((tag) => /^v\d+\.\d+\.\d+$/.test(tag.trim()))?.trim();
}
