// The flag list's query string. No backoffice imports, so Node can test it.
import type { FlagQuery } from "./types.js";

export function flagsQueryString(query: FlagQuery): string {
  const parameters = new URLSearchParams();
  parameters.set("skip", String(query.skip ?? 0));
  parameters.set("take", String(query.take ?? 25));
  for (const key of ["filter", "state", "show", "tag", "orderBy"] as const) {
    const value = query[key];
    if (value) parameters.set(key, value);
  }
  if (query.marks?.length) parameters.set("marks", query.marks.join(","));
  // An empty section asks for flags in no section, so it is sent even when empty.
  if (query.section !== undefined && query.section !== null) parameters.set("section", query.section);
  return parameters.toString();
}
