// Compile-time checks, run by `tsc --noEmit`: the types package's shapes and the client's own must be assignable both
// ways, so neither drifts without this failing.
import type { FeatureFlag, FlagSettings, FlagCondition, FlagActions } from "../index.d.ts";
import type {
  FeatureFlag as ClientFlag,
  FlagSettings as ClientSettings,
  FlagCondition as ClientCondition,
  FlagActions as ClientActions,
} from "../../Umbricians.FeatureFlags/Client/src/api/types.ts";

type Same<A, B> = [A] extends [B] ? ([B] extends [A] ? true : false) : false;
const check = <T extends true>() => undefined as unknown as T;

check<Same<FeatureFlag, Omit<ClientFlag, "state" | "defaultState"> & Pick<FeatureFlag, "state" | "defaultState">>>();
check<Same<FeatureFlag["state"], ClientFlag["state"]>>();
check<Same<FlagSettings, ClientSettings>>();
check<Same<FlagCondition, ClientCondition>>();
check<Same<FlagActions, ClientActions>>();
