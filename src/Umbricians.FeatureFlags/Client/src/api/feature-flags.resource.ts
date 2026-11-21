import { umbHttpClient } from "@umbraco-cms/backoffice/http-client";
import { FeatureFlagsRequestError } from "./request-error.js";
import type {
  FeatureFlag,
  FeatureFlagListItem,
  FeatureFlagSummary,
  FlagAudience,
  FlagQuery,
  PagedResult,
  ProviderGroup,
} from "./types.js";

export type * from "./types.js";

const BASE_URL = "/umbraco/umbriciansfeatureflags/api/v1/";

/**
 * Calls the Feature Flags API through the backoffice's own HTTP client, so requests carry the user's session and an
 * expired access token is refreshed and the request retried, exactly as for core calls.
 */
async function request<T>(method: "GET" | "PUT" | "DELETE", path: string, body?: unknown): Promise<T> {
  const { data, error, response } = await umbHttpClient.request({
    method,
    url: BASE_URL + path,
    // Tells the client to apply the backoffice authentication to this call.
    security: [{ type: "http", scheme: "bearer" }],
    // Return the error body rather than throwing it as it is, so every failure becomes a FeatureFlagsRequestError.
    throwOnError: false,
    ...(body === undefined ? {} : { body }),
  });

  if (error !== undefined || !response?.ok) {
    const detail = (error as { detail?: unknown } | undefined)?.detail;
    throw new FeatureFlagsRequestError(response?.status, typeof detail === "string" && detail ? detail : undefined);
  }
  return data as T;
}

/** Flag names go in the query string, never the path: they may hold dots, slashes and spaces. */
const nameQuery = (name: string) => `name=${encodeURIComponent(name)}`;

export function flagsQueryString(query: FlagQuery): string {
  const parameters = new URLSearchParams();
  parameters.set("skip", String(query.skip ?? 0));
  parameters.set("take", String(query.take ?? 25));
  for (const key of ["filter", "state", "show", "tag", "orderBy"] as const) {
    const value = query[key];
    if (value) parameters.set(key, value);
  }
  // An empty section asks for flags in no section, so it is sent even when empty.
  if (query.section !== undefined && query.section !== null) parameters.set("section", query.section);
  return parameters.toString();
}

export const fetchFlags = (query: FlagQuery) =>
  request<PagedResult<FeatureFlagListItem>>("GET", `flags?${flagsQueryString(query)}`);

export const fetchSummary = () => request<FeatureFlagSummary>("GET", "flags/summary");

export const fetchFlag = (name: string) => request<FeatureFlag>("GET", `flag?${nameQuery(name)}`);

export const fetchProviderGroups = () => request<Array<ProviderGroup>>("GET", "provider-groups");

/** Saves an editor's settings: on or off, and who for (null for everyone). */
export const saveFlag = (name: string, enabled: boolean, audience: FlagAudience | null) =>
  request<void>("PUT", `flag?${nameQuery(name)}`, { enabled, audience });

/** Switches a flag on or off, keeping who it is for. */
export const setFlagEnabled = (name: string, enabled: boolean) =>
  request<void>("PUT", `flag/enabled?${nameQuery(name)}`, { enabled });

/** Resets a flag to its default, or removes the setting of a flag nobody declares any more. */
export const resetFlag = (name: string) => request<void>("DELETE", `flag?${nameQuery(name)}`);
