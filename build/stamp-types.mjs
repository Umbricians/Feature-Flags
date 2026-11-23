// Writes the package version and the Umbraco version it supports into the npm types package's package.json.
// The backoffice peer dependency is ^<Umbraco version>, the lowest version of the major the package is built for.
//
//   node build/stamp-types.mjs <package.json> <version> <Umbraco version>
import { readFileSync, writeFileSync } from "node:fs";

const [, , packagePath, version, umbracoVersion] = process.argv;
if (!packagePath || !version || !umbracoVersion) {
  console.error("Usage: node stamp-types.mjs <package.json> <version> <Umbraco version>");
  process.exit(1);
}

const manifest = JSON.parse(readFileSync(packagePath, "utf8"));
manifest.version = version;
manifest.peerDependencies["@umbraco-cms/backoffice"] = `^${umbracoVersion}`;
writeFileSync(packagePath, JSON.stringify(manifest, null, 2) + "\n");
