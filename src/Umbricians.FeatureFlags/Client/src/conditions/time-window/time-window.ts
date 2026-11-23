// The time window condition's logic: reading the times a flag stores, converting them to and from the editor's local
// time, and saying what is wrong with a window. No backoffice imports, so Node can test it. A time zone is passed in as
// a function from an instant to its offset, so tests don't depend on the machine's own.

/** The offset from UTC, in minutes east of it, that a time zone has at an instant (epoch milliseconds). */
export type OffsetAt = (instant: number) => number;

/** The browser's time zone. */
export const browserOffsetAt: OffsetAt = (instant) => -new Date(instant).getTimezoneOffset();

/** What a window's Start or End holds: nothing, a time, or text that isn't a time this editor can read. */
export type WindowEnd = { kind: "none" } | { kind: "at"; instant: number } | { kind: "unreadable"; text: string };

export type WindowEndName = "Start" | "End";

/** What is wrong with a window, for the element to say in words. */
export type WindowProblem = "neither" | "startNotBeforeEnd" | "unreadableStart" | "unreadableEnd";

// A time of day and a zone: ISO 8601 ("2026-12-01T09:00:00Z", "...+01:00") or RFC 1123 ("... GMT"). A time without a zone
// would be read in the server's own time zone, which the editor can't know, so it isn't read here.
const withZone = /\d:\d{2}(:\d{2}(\.\d+)?)?\s*(Z|GMT|UTC|UT|[+-]\d{2}(:?\d{2})?)$/i;

/** The instant a stored time means, or null when it isn't a time with a zone. */
export function parseInstant(text: string): number | null {
  const trimmed = text.trim();
  if (!withZone.test(trimmed)) return null;
  const instant = Date.parse(trimmed);
  return Number.isNaN(instant) ? null : instant;
}

/** Reads the Start or End of a window's parameters, ignoring the case of the key as Microsoft's configuration does. */
export function readEnd(parameters: Record<string, unknown>, end: WindowEndName): WindowEnd {
  const key = Object.keys(parameters).find((name) => name.toLowerCase() === end.toLowerCase());
  const raw = key === undefined ? undefined : parameters[key];
  if (raw === undefined || raw === null || (typeof raw === "string" && raw.trim() === "")) return { kind: "none" };
  if (typeof raw !== "string") return { kind: "unreadable", text: String(raw) };
  const instant = parseInstant(raw);
  return instant === null ? { kind: "unreadable", text: raw } : { kind: "at", instant };
}

/** What the backoffice stores for an instant: ISO 8601 in UTC ending in Z, such as `2026-12-01T09:00:00Z`. */
export function toUtcIso(instant: number): string {
  return new Date(instant).toISOString().replace(/\.\d{3}Z$/, "Z");
}

const pad = (value: number, width = 2) => String(value).padStart(width, "0");

/** An instant as a `datetime-local` input's value, in the time zone: `2026-12-01T10:00`. */
export function toLocalInput(instant: number, offsetAt: OffsetAt): string {
  const wall = new Date(instant + offsetAt(instant) * 60_000);
  return `${pad(wall.getUTCFullYear(), 4)}-${pad(wall.getUTCMonth() + 1)}-${pad(wall.getUTCDate())}T${pad(wall.getUTCHours())}:${pad(wall.getUTCMinutes())}`;
}

/**
 * The instant a `datetime-local` input's value means in the time zone, or null for an empty or unreadable value. When
 * the zone changes offset in between (daylight saving), the offset at the resulting instant is used.
 */
export function fromLocalInput(text: string, offsetAt: OffsetAt): number | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(text.trim());
  if (!match) return null;
  const [year, month, day, hour, minute, second] = match.slice(1).map((part) => Number(part ?? 0));
  const wall = Date.UTC(year, month - 1, day, hour, minute, second);
  const check = new Date(wall);
  if (check.getUTCMonth() !== month - 1 || check.getUTCDate() !== day) return null;
  const guess = wall - offsetAt(wall) * 60_000;
  return wall - offsetAt(guess) * 60_000;
}

/** An instant in UTC for showing beside a local time: `2026-12-01 09:00 UTC`. */
export function formatUtc(instant: number): string {
  return `${toUtcIso(instant).slice(0, 16).replace("T", " ")} UTC`;
}

/** An offset in minutes as `UTC+01:00`, `UTC-05:00` or `UTC`. */
export function formatOffset(minutes: number): string {
  if (minutes === 0) return "UTC";
  const sign = minutes < 0 ? "-" : "+";
  const absolute = Math.abs(minutes);
  return `UTC${sign}${pad(Math.floor(absolute / 60))}:${pad(absolute % 60)}`;
}

/** The parameters with one end set to an instant, or taken out when null. Other parameters, such as a recurrence, are kept. Always a new object. */
export function withEnd(parameters: Record<string, unknown>, end: WindowEndName, instant: number | null): Record<string, unknown> {
  const key = Object.keys(parameters).find((name) => name.toLowerCase() === end.toLowerCase()) ?? end;
  const { [key]: _removed, ...rest } = parameters;
  return instant === null ? rest : { ...rest, [end]: toUtcIso(instant) };
}

/** What is wrong with a window: the same things the server refuses. None means it is fine. */
export function windowProblems(parameters: Record<string, unknown>): Array<WindowProblem> {
  const start = readEnd(parameters, "Start");
  const end = readEnd(parameters, "End");
  const problems: Array<WindowProblem> = [];
  if (start.kind === "unreadable") problems.push("unreadableStart");
  if (end.kind === "unreadable") problems.push("unreadableEnd");
  if (start.kind === "none" && end.kind === "none") problems.push("neither");
  if (start.kind === "at" && end.kind === "at" && start.instant >= end.instant) problems.push("startNotBeforeEnd");
  return problems;
}
