// Type-checks index.d.ts itself, which `tsc --noEmit` skips (skipLibCheck, needed because Umbraco's own declarations
// don't all pass). Checks everything, then fails only for problems in this package's own files.
import { spawnSync } from "node:child_process";

const result = spawnSync("npx", ["tsc", "--noEmit", "--skipLibCheck", "false"], { encoding: "utf8", shell: process.platform === "win32" });
const own = (result.stdout + result.stderr).split("\n").filter((line) => /^(index\.d\.ts|test\/)/.test(line));
if (own.length > 0) {
  console.error(own.join("\n"));
  process.exit(1);
}
console.log("index.d.ts type-checks.");
