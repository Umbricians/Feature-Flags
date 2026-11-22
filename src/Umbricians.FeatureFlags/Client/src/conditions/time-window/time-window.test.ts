import { test } from "node:test";
import assert from "node:assert/strict";
import {
  formatOffset,
  formatUtc,
  fromLocalInput,
  parseInstant,
  readEnd,
  toLocalInput,
  toUtcIso,
  windowProblems,
  withEnd,
  type OffsetAt,
} from "./time-window.ts";

const utc: OffsetAt = () => 0;
const paris: OffsetAt = () => 60;
const newYork: OffsetAt = () => -300;
const kolkata: OffsetAt = () => 330;
// A zone that springs forward at 2026-03-29 01:00 UTC, from +01:00 to +02:00, as Paris does.
const springForward: OffsetAt = (instant) => (instant >= Date.UTC(2026, 2, 29, 1) ? 120 : 60);

const nineAmUtc = Date.UTC(2026, 11, 1, 9, 0, 0);

test("ISO 8601 and RFC 1123 times are read, whatever zone they are written in", () => {
  assert.equal(parseInstant("2026-12-01T09:00:00Z"), nineAmUtc);
  assert.equal(parseInstant("2026-12-01T10:00:00+01:00"), nineAmUtc);
  assert.equal(parseInstant("2026-12-01T04:00:00-05:00"), nineAmUtc);
  assert.equal(parseInstant("2026-12-01T09:00Z"), nineAmUtc);
  assert.equal(parseInstant("Tue, 01 Dec 2026 09:00:00 GMT"), nineAmUtc);
});

test("a time without a zone, or that isn't a time, is not read", () => {
  assert.equal(parseInstant("2026-12-01T09:00:00"), null, "the server would read it in its own zone");
  assert.equal(parseInstant("2026-12-01"), null);
  assert.equal(parseInstant("next tuesday"), null);
  assert.equal(parseInstant("2026-13-45T09:00:00Z"), null);
  assert.equal(parseInstant(""), null);
});

test("an end is read from the parameters, ignoring the case of its key", () => {
  assert.deepEqual(readEnd({ Start: "2026-12-01T09:00:00Z" }, "Start"), { kind: "at", instant: nineAmUtc });
  assert.deepEqual(readEnd({ start: "2026-12-01T09:00:00Z" }, "Start"), { kind: "at", instant: nineAmUtc });
  assert.deepEqual(readEnd({}, "End"), { kind: "none" });
  assert.deepEqual(readEnd({ End: null }, "End"), { kind: "none" });
  assert.deepEqual(readEnd({ End: "  " }, "End"), { kind: "none" });
  assert.deepEqual(readEnd({ End: "soon" }, "End"), { kind: "unreadable", text: "soon" });
  assert.deepEqual(readEnd({ End: 5 }, "End"), { kind: "unreadable", text: "5" });
});

test("times are stored as ISO 8601 in UTC ending in Z, without fractions of a second", () => {
  assert.equal(toUtcIso(nineAmUtc), "2026-12-01T09:00:00Z");
  assert.equal(toUtcIso(nineAmUtc + 30_000), "2026-12-01T09:00:30Z");
});

test("an instant is shown as local time for the zone, with the UTC equivalent beside it", () => {
  assert.equal(toLocalInput(nineAmUtc, utc), "2026-12-01T09:00");
  assert.equal(toLocalInput(nineAmUtc, paris), "2026-12-01T10:00");
  assert.equal(toLocalInput(nineAmUtc, newYork), "2026-12-01T04:00");
  assert.equal(toLocalInput(nineAmUtc, kolkata), "2026-12-01T14:30");
  assert.equal(formatUtc(nineAmUtc), "2026-12-01 09:00 UTC");
});

test("a local time is converted back to the instant it means in the zone", () => {
  assert.equal(fromLocalInput("2026-12-01T10:00", paris), nineAmUtc);
  assert.equal(fromLocalInput("2026-12-01T04:00", newYork), nineAmUtc);
  assert.equal(fromLocalInput("2026-12-01T14:30", kolkata), nineAmUtc);
  assert.equal(fromLocalInput("2026-12-01T09:00:00", utc), nineAmUtc);
});

test("local time round-trips through UTC in every zone, either side of a clock change", () => {
  const instants = [nineAmUtc, Date.UTC(2026, 2, 29, 0, 30), Date.UTC(2026, 2, 29, 1, 0), Date.UTC(2026, 6, 1, 12, 15)];
  for (const zone of [utc, paris, newYork, kolkata, springForward]) {
    for (const instant of instants) {
      assert.equal(fromLocalInput(toLocalInput(instant, zone), zone), instant);
    }
  }
});

test("a local time that does not exist, or is empty, is not converted", () => {
  assert.equal(fromLocalInput("", utc), null);
  assert.equal(fromLocalInput("2026-02-31T10:00", utc), null);
  assert.equal(fromLocalInput("not a time", utc), null);
});

test("offsets are written the way editors read them", () => {
  assert.equal(formatOffset(0), "UTC");
  assert.equal(formatOffset(60), "UTC+01:00");
  assert.equal(formatOffset(-300), "UTC-05:00");
  assert.equal(formatOffset(330), "UTC+05:30");
});

test("setting an end writes a new object, keeps other parameters, and clearing takes the end out", () => {
  const original = Object.freeze({ Start: "Tue, 01 Dec 2026 09:00:00 GMT", Recurrence: { Pattern: {} } });
  const set = withEnd(original, "End", nineAmUtc + 3_600_000);
  assert.deepEqual(set, { Start: "Tue, 01 Dec 2026 09:00:00 GMT", Recurrence: { Pattern: {} }, End: "2026-12-01T10:00:00Z" });
  assert.notEqual(set, original);

  const replaced = withEnd({ start: "Tue, 01 Dec 2026 09:00:00 GMT" }, "Start", nineAmUtc);
  assert.deepEqual(replaced, { Start: "2026-12-01T09:00:00Z" }, "a key written in another case is replaced, not duplicated");

  assert.deepEqual(withEnd({ Start: "2026-12-01T09:00:00Z", End: "2026-12-02T09:00:00Z" }, "End", null), { Start: "2026-12-01T09:00:00Z" });
});

test("a window is checked the way the server checks it", () => {
  assert.deepEqual(windowProblems({}), ["neither"]);
  assert.deepEqual(windowProblems({ Start: "2026-12-01T09:00:00Z" }), []);
  assert.deepEqual(windowProblems({ End: "2026-12-01T09:00:00Z" }), []);
  assert.deepEqual(windowProblems({ Start: "2026-12-02T09:00:00Z", End: "2026-12-01T09:00:00Z" }), ["startNotBeforeEnd"]);
  assert.deepEqual(windowProblems({ Start: "2026-12-01T09:00:00Z", End: "2026-12-01T09:00:00Z" }), ["startNotBeforeEnd"]);
  assert.deepEqual(windowProblems({ Start: "2026-12-01T10:00:00+01:00", End: "2026-12-01T09:30:00Z" }), []);
  assert.deepEqual(windowProblems({ Start: "later", End: "2026-12-01T09:00:00Z" }), ["unreadableStart"]);
  assert.deepEqual(windowProblems({ Start: "later", End: "sooner" }), ["unreadableStart", "unreadableEnd"]);
});
