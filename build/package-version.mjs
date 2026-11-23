// Prints the version a CI build packs for one Umbraco major, from GitHub's ref variables and the repository's tags.
//
//   node build/package-version.mjs <Umbraco major>
//
// The package's major is the Umbraco major it supports, and each major is built separately (-p:UmbracoMajor=18).
// A tag v18.2.3 builds 18.2.3, and fails for any other major. Any other build is {next patch}-ci.{run number}, where
// the next patch follows the highest release or pre-release tag of that major (v19.1.0-rc.1 counts as 19.1.0), or is
// {major}.0.0 before its first tag. Counting pre-releases keeps a CI build above the package validation baseline of a
// major that has only pre-releases so far.
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
  const last = lastTaggedVersion();
  if (!last) return undefined;
  const [major, minor, patch] = last;
  return `${major}.${minor}.${patch + 1}`;
}

// The highest major.minor.patch of this major's tags in the repository, reachable or not: Gitflow tags releases on
// main before develop has them. A pre-release tag counts by its major.minor.patch. Other majors' tags don't count.
function lastTaggedVersion() {
  const tags = execFileSync("git", ["tag", "--list", `v${umbracoMajor}.*.*`], { encoding: "utf8" });
  return tags
    .split("\n")
    .map((tag) => /^v(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?$/.exec(tag.trim()))
    .filter(Boolean)
    .map((match) => match.slice(1, 4).map(Number))
    .sort((a, b) => b[0] - a[0] || b[1] - a[1] || b[2] - a[2])[0];
}
