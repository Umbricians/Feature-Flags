// What the Root condition holds and how the editor changes it. No backoffice imports, so Node can test it.
import type { SiteRoot } from "../../api/types.js";

/** One line of the picker: a site the condition names or the site could name. */
export interface RootRow {
  key: string;
  /** The root's name; null for a root that has been deleted, which the editor shows as "a deleted site". */
  name: string | null;
  domains: Array<string>;
  deleted: boolean;
  selected: boolean;
}

/** The keys in the condition's `Roots`, whatever case the property is written in. Anything that isn't text is ignored. */
export function readRoots(value: Record<string, unknown>): Array<string> {
  const property = Object.keys(value).find((key) => key.toLowerCase() === "roots");
  const list = property ? value[property] : undefined;
  return Array.isArray(list) ? list.filter((item): item is string => typeof item === "string") : [];
}

const same = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();

/** The parameters with one root chosen or not, keeping what was already there. */
export function withRoot(value: Record<string, unknown>, key: string, chosen: boolean): Record<string, unknown> {
  const current = readRoots(value);
  const roots = chosen
    ? current.some((item) => same(item, key))
      ? current
      : [...current, key]
    : current.filter((item) => !same(item, key));
  return { Roots: roots };
}

/**
 * Every root the site has, with the chosen ones marked, then the chosen keys no root has any more as deleted sites,
 * so an editor can see and remove them.
 */
export function rootRows(chosen: ReadonlyArray<string>, all: ReadonlyArray<SiteRoot>): Array<RootRow> {
  const rows = all.map(
    (root): RootRow => ({ key: root.key, name: root.name ?? null, domains: root.domains, deleted: false, selected: chosen.some((key) => same(key, root.key)) }),
  );
  const deleted = chosen
    .filter((key) => !all.some((root) => same(root.key, key)))
    .map((key): RootRow => ({ key, name: null, domains: [], deleted: true, selected: true }));
  return [...rows, ...deleted];
}
