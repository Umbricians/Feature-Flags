// Starts the built demo site, checks it serves the backoffice, the package's bundle and the package's API document,
// which only exists once the package's composer has run, then stops it.
//
//   node build/smoke-test.mjs <demo project folder> <bundle URL path> <API document URL path> [Umbraco major]
//
// The API document is /umbraco/swagger/{name}/swagger.json on Umbraco 17 and /umbraco/openapi/{name}.json from 18.
// Run it after `dotnet build -c Release -p:UmbracoMajor=…` for the same major (default 17). In Development the demo
// installs itself, here on a fresh SQLite database of its own.
import { execFileSync, spawn } from "node:child_process";
import { mkdtempSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";

const SITE_URL = "http://127.0.0.1:5080";
const STARTUP_TIMEOUT_MS = 180_000;
const POLL_INTERVAL_MS = 2_000;

const [, , demoFolder, bundlePath, documentPath, umbracoMajor = "17"] = process.argv;
if (!demoFolder || !bundlePath || !documentPath?.startsWith("/")) {
  console.error("Usage: node smoke-test.mjs <demo project folder> <bundle URL path> <API document URL path> [Umbraco major]");
  process.exit(1);
}

const projectDir = resolve(demoFolder);
// Where MSBuild put the demo for this major (Directory.Build.props gives each major its own bin folder).
const assembly = execFileSync(
  "dotnet",
  ["msbuild", projectDir, "-getProperty:TargetPath", "-p:Configuration=Release", `-p:UmbracoMajor=${umbracoMajor}`],
  { encoding: "utf8" },
).trim();
console.log(`Starting ${assembly}`);
// Each run installs on its own new database: the demo's own one is a different Umbraco major's after another major ran
// against it, which Umbraco refuses to start on.
const databaseDir = mkdtempSync(join(tmpdir(), "umbricians-smoke-"));
const site = spawn("dotnet", [assembly, "--urls", SITE_URL], {
  cwd: projectDir,
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: "Development",
    DOTNET_ENVIRONMENT: "Development",
    ConnectionStrings__umbracoDbDSN: `Data Source=${join(databaseDir, "Umbraco.sqlite.db")};Foreign Keys=True;Pooling=True`,
    ConnectionStrings__umbracoDbDSN_ProviderName: "Microsoft.Data.Sqlite",
  },
  stdio: "inherit",
});

try {
  await waitUntilOk(`${SITE_URL}/umbraco`);
  await expectOk(`${SITE_URL}${bundlePath}`);
  await expectOk(`${SITE_URL}${documentPath}`);
  console.log("The demo serves the backoffice, the package's bundle and its API document.");
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  site.kill();
  await new Promise((done) => (site.exitCode === null ? site.once("exit", done) : done()));
  rmSync(databaseDir, { recursive: true, force: true });
}

async function waitUntilOk(url) {
  const deadline = Date.now() + STARTUP_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if (site.exitCode !== null) throw new Error(`The demo exited with code ${site.exitCode} before ${url} answered.`);
    if (await isOk(url)) return;
    await new Promise((done) => setTimeout(done, POLL_INTERVAL_MS));
  }
  throw new Error(`${url} didn't answer within ${STARTUP_TIMEOUT_MS / 1000} seconds.`);
}

async function expectOk(url) {
  if (!(await isOk(url))) throw new Error(`${url} didn't return a success status.`);
}

async function isOk(url) {
  try {
    return (await fetch(url)).ok;
  } catch {
    return false; // The site isn't listening yet.
  }
}
