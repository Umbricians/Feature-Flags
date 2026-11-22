// How the editor tells conditions apart by name. No backoffice imports, so Node can test it.
import type { FlagCondition, RegisteredCondition } from "../api/types.js";

/** The name of the package's own Root condition, which limits a flag to chosen sites. */
export const ROOT_CONDITION = "Umbricians.Root";

/**
 * Whether a filter name as a flag writes it is the registered name, the way Microsoft.FeatureManagement matches filter
 * names: ignoring case, and a name without a namespace matches the last segment of the registered name, so `Targeting`
 * matches `Microsoft.Targeting` and `Culture` matches `Contoso.Culture`.
 */
export function namesMatch(registered: string, used: string): boolean {
  const left = registered.toLowerCase();
  const right = used.toLowerCase();
  return left === right || (!right.includes(".") && left.endsWith("." + right));
}

/** The first item whose name matches, preferring an exact match over a short one, as the server does. */
export function findByName<T>(items: ReadonlyArray<T>, nameOf: (item: T) => string, used: string): T | undefined {
  return items.find((item) => nameOf(item).toLowerCase() === used.toLowerCase()) ?? items.find((item) => namesMatch(nameOf(item), used));
}

export function isRoot(name: string): boolean {
  return namesMatch(ROOT_CONDITION, name);
}

/** What editors call a flag's condition: the registered display name, or the filter's name as the flag writes it. */
export function displayNameOf(registered: ReadonlyArray<RegisteredCondition>, condition: FlagCondition): string {
  return findByName(registered, (item) => item.name, condition.name)?.displayName ?? condition.name;
}

/**
 * Whether a manifest for a condition's editor exists, given the `meta.name` of every manifest of the type.
 * Manifests are matched to the condition's name the same way the server matches registered conditions.
 */
export function hasEditor(editorNames: ReadonlyArray<string>, name: string): boolean {
  return findByName(editorNames, (editor) => editor, name) !== undefined;
}

/**
 * The conditions the "Add condition" menu offers: registered on the server, with an editor, and not already on the flag.
 */
export function addableConditions(
  registered: ReadonlyArray<RegisteredCondition>,
  editorNames: ReadonlyArray<string>,
  conditions: ReadonlyArray<FlagCondition>,
): Array<RegisteredCondition> {
  return registered.filter(
    (item) => hasEditor(editorNames, item.name) && !conditions.some((condition) => namesMatch(item.name, condition.name)),
  );
}
