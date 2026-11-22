// The flag editor's unsaved state, as immutable functions. No backoffice imports, so Node can test it.
import type { ConditionMatch, FeatureFlag, FlagCondition, SaveSettings } from "../../api/types.js";
import { emptyAudience, isTargeting, toTargetingCondition } from "../audience.js";
import { hasEditor, isRoot, namesMatch } from "../../conditions/condition-names.js";
import { mayDo } from "../display.js";

/** What the editor's state choice means: off (keeping its conditions), on for everyone, or on with conditions. */
export type FlagEditorMode = "off" | "everyone" | "conditions";

export interface FlagEditorState {
  readonly flag: FeatureFlag;
  readonly mode: FlagEditorMode;
  /** The conditions being edited; kept while the flag is off or on for everyone, so switching back restores them. */
  readonly conditions: ReadonlyArray<FlagCondition>;
  /** Whether a flag with several conditions needs any one of them, or all. */
  readonly match: ConditionMatch;
  /** Whether adding the Root condition just switched the match to All, which the editor says. */
  readonly matchSwitchedForRoot: boolean;
  /** Whether what saving would send differs from what was loaded. */
  readonly dirty: boolean;
  readonly loaded: SaveSettings;
}

/** Why the editor can't save what it holds, though the catalog allows saving. */
export type SaveBlocker =
  /** A condition is registered in C# but no manifest edits it. */
  | { kind: "noEditor"; names: Array<string> }
  /** "On with conditions" holds none. */
  | { kind: "noConditions" };

/** The editor for a flag as the server has it, with nothing changed. */
export function loadEditor(flag: FeatureFlag): FlagEditorState {
  const conditions = flag.settings?.conditions ?? [];
  const match = flag.settings?.match ?? "any";
  const mode: FlagEditorMode = !flag.enabled ? "off" : conditions.length > 0 ? "conditions" : "everyone";
  const state: FlagEditorState = { flag, mode, conditions, match, matchSwitchedForRoot: false, dirty: false, loaded: { enabled: false, conditions: [], match } };
  return { ...state, loaded: savePayload(state) };
}

export function setMode(state: FlagEditorState, mode: FlagEditorMode): FlagEditorState {
  return changed({ ...state, mode });
}

/**
 * Adds a condition by the name it is registered under, with the parameters an editor starts from, and switches the
 * editor to "on with conditions". A condition already on the flag is not added twice. Adding the Root condition to a
 * flag that already has another switches the match to All, and says so until the editor changes it.
 */
export function addCondition(state: FlagEditorState, name: string, parameters?: Record<string, unknown>): FlagEditorState {
  if (state.conditions.some((condition) => namesMatch(name, condition.name))) return changed({ ...state, mode: "conditions" });
  const condition: FlagCondition = { name, parameters: parameters ?? startingParameters(name) };
  const switchToAll = isRoot(name) && state.conditions.length > 0;
  return changed({
    ...state,
    mode: "conditions",
    conditions: [...state.conditions, condition],
    match: switchToAll ? "all" : state.match,
    matchSwitchedForRoot: switchToAll || state.matchSwitchedForRoot,
  });
}

export function removeCondition(state: FlagEditorState, index: number): FlagEditorState {
  const conditions = state.conditions.filter((_, at) => at !== index);
  return changed({ ...state, conditions, matchSwitchedForRoot: state.matchSwitchedForRoot && conditions.some((c) => isRoot(c.name)) });
}

export function setConditionParameters(state: FlagEditorState, index: number, parameters: Record<string, unknown>): FlagEditorState {
  return changed({
    ...state,
    conditions: state.conditions.map((condition, at) => (at === index ? { ...condition, parameters } : condition)),
  });
}

export function setMatch(state: FlagEditorState, match: ConditionMatch): FlagEditorState {
  return changed({ ...state, match, matchSwitchedForRoot: false });
}

/** What a condition's editor starts from: the audience's empty targeting parameters, and nothing for any other. */
function startingParameters(name: string): Record<string, unknown> {
  return isTargeting({ name, parameters: {} }) ? toTargetingCondition(emptyAudience()).parameters : {};
}

/**
 * What saving sends: whether the flag is on, and its conditions and how they match. Only choosing "on for everyone"
 * clears the conditions; switching off keeps them.
 */
export function savePayload(state: FlagEditorState): SaveSettings {
  const { mode, match } = state;
  if (mode === "everyone") return { enabled: true, conditions: [], match };
  return { enabled: mode === "conditions", conditions: state.conditions.map((c) => ({ ...c })), match };
}

/** Whether the flag is one the editor can't read at all: on with a filter nobody registered a condition for. */
export function isCustom(flag: FeatureFlag): boolean {
  return flag.state === "custom";
}

/**
 * Why saving is refused beyond what the catalog says, or null. A condition with no editor can't be saved from here
 * unless "on for everyone" replaces it; and "on with conditions" needs at least one.
 */
export function saveBlocker(state: FlagEditorState, editorNames: ReadonlyArray<string>): SaveBlocker | null {
  if (state.mode === "everyone") return null;
  const without = state.conditions.filter((condition) => !hasEditor(editorNames, condition.name)).map((condition) => condition.name);
  if (without.length > 0) return { kind: "noEditor", names: without };
  return state.mode === "conditions" && state.conditions.length === 0 ? { kind: "noConditions" } : null;
}

/** The catalog allows saving, the user may change flags, nothing blocks it, and something changed. */
export function canSave(state: FlagEditorState, canManage: boolean, editorNames: ReadonlyArray<string>): boolean {
  return mayDo(state.flag, "save", canManage).allowed && state.dirty && saveBlocker(state, editorNames) === null;
}

function changed(state: FlagEditorState): FlagEditorState {
  return { ...state, dirty: !samePayload(savePayload(state), state.loaded) };
}

/** Saved settings are the same when they say the same: the order of values inside parameters isn't a change, and nor is the match of fewer than two conditions. */
function samePayload(a: SaveSettings, b: SaveSettings): boolean {
  const matchOf = (settings: SaveSettings) => (settings.conditions.length < 2 ? "any" : settings.match);
  return (
    a.enabled === b.enabled &&
    matchOf(a) === matchOf(b) &&
    a.conditions.length === b.conditions.length &&
    a.conditions.every(
      (condition, at) =>
        condition.name === b.conditions[at].name &&
        canonical(condition.parameters, isTargeting(condition)) === canonical(b.conditions[at].parameters, isTargeting(b.conditions[at])),
    )
  );
}

/**
 * JSON with keys sorted. Arrays are sorted by their own canonical form only when `sets` says their order means nothing,
 * as in an audience; the server compares every other condition's arrays in order, so the editor does too.
 */
function canonical(value: unknown, sets: boolean): string {
  if (Array.isArray(value)) {
    const items = value.map((item) => canonical(item, sets));
    return `[${(sets ? items.sort() : items).join(",")}]`;
  }
  if (typeof value === "object" && value !== null) {
    return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonical((value as Record<string, unknown>)[key], sets)}`).join(",")}}`;
  }
  return JSON.stringify(value);
}
