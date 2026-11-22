// The flag editor's unsaved state, as immutable functions. No backoffice imports, so Node can test it.
import type { FeatureFlag, FlagAudience, ProviderGroup, SaveSettings } from "../../api/types.js";
import { emptyAudience, isTargeting, toTargetingCondition, withMemberGroupKeys, withoutGroup, withProviderGroup } from "../audience.js";
import { mayDo } from "../display.js";

/** What the editor's state choice means: off (keeping any audience), on for everyone, or on for an audience. */
export type FlagEditorMode = "off" | "everyone" | "audience";

export interface FlagEditorState {
  readonly flag: FeatureFlag;
  readonly mode: FlagEditorMode;
  /** The audience being edited; kept while the flag is off, so switching it back on restores it. */
  readonly audience: FlagAudience;
  /** Whether the mode or audience differs from what was loaded. */
  readonly dirty: boolean;
  readonly loaded: { readonly mode: FlagEditorMode; readonly audience: FlagAudience };
}

/** The editor for a flag as the server has it, with nothing changed. */
export function loadEditor(flag: FeatureFlag): FlagEditorState {
  const mode: FlagEditorMode = !flag.enabled ? "off" : flag.audienceDetail ? "audience" : "everyone";
  const audience = flag.audienceDetail ?? emptyAudience();
  return { flag, mode, audience, dirty: false, loaded: { mode, audience } };
}

export function setMode(state: FlagEditorState, mode: FlagEditorMode): FlagEditorState {
  return changed({ ...state, mode });
}

export function setMembers(state: FlagEditorState, members: Array<string>): FlagEditorState {
  return changed({ ...state, audience: { ...state.audience, members } });
}

export function setMemberGroupKeys(state: FlagEditorState, keys: Array<string>): FlagEditorState {
  return changed({ ...state, audience: withMemberGroupKeys(state.audience, keys) });
}

export function setProviderGroup(state: FlagEditorState, group: ProviderGroup, checked: boolean): FlagEditorState {
  return changed({ ...state, audience: withProviderGroup(state.audience, group, checked) });
}

export function removeGroup(state: FlagEditorState, id: string): FlagEditorState {
  return changed({ ...state, audience: withoutGroup(state.audience, id) });
}

/**
 * What saving sends: whether the flag is on, and its conditions. The audience is written as a Microsoft.Targeting
 * condition; every other condition the flag has, and how they match, is kept as it is, so saving never drops what the
 * editor can't change here. Only choosing "everyone" clears the conditions.
 */
export function savePayload(state: FlagEditorState): SaveSettings {
  const { mode, audience } = state;
  const settings = state.flag.settings;
  const match = settings?.match ?? "any";
  if (mode === "everyone") return { enabled: true, conditions: [], match };
  const others = (settings?.conditions ?? []).filter((condition) => !isTargeting(condition));
  const kept = audience.members.length + audience.groups.length > 0;
  const hasAudience = mode === "audience" || kept || state.flag.audienceDetail !== null;
  return {
    enabled: mode === "audience",
    conditions: hasAudience ? [toTargetingCondition(audience), ...others] : others,
    match,
  };
}

/**
 * Whether the flag has conditions this editor can't change: any besides its one audience, read from its settings and
 * not from its state, since a flag that is switched off keeps its conditions too.
 */
export function hasOtherConditions(flag: FeatureFlag): boolean {
  const conditions = flag.settings?.conditions ?? [];
  return flag.state === "conditions" || conditions.some((condition) => !isTargeting(condition)) || conditions.length > 1;
}

/** The catalog allows saving, the user may change flags, and something changed. */
export function canSave(state: FlagEditorState, canManage: boolean): boolean {
  return mayDo(state.flag, "save", canManage).allowed && state.dirty;
}

function changed(state: FlagEditorState): FlagEditorState {
  const { loaded } = state;
  const dirty = state.mode !== loaded.mode || !sameAudience(state.audience, loaded.audience);
  return { ...state, dirty };
}

function sameAudience(a: FlagAudience, b: FlagAudience): boolean {
  return sameSet(a.members, b.members) && sameSet(a.groups.map((g) => g.id), b.groups.map((g) => g.id));
}

function sameSet(a: Array<string>, b: Array<string>): boolean {
  const left = new Set(a);
  const right = new Set(b);
  return left.size === right.size && [...left].every((value) => right.has(value));
}
