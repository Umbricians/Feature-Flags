import type { UmbLocalizationDictionary } from "@umbraco-cms/backoffice/localization-api";

// Every term is used as umbriciansFeatureFlags_<key>. Write each key out in full where it is used, never built from
// parts: the test that checks every key has a term and every term is used finds them by searching for the literal.
// A term with a count is a function of that count, so a language can use its own plural forms.
const plural = (count: number, one: string, many: string) => `${count} ${count === 1 ? one : many}`;

export default {
  user: {
    // The user group editor heads each permission box with user_permissionsEntityGroup_<entity type>; must match
    // FEATURE_FLAGS_PERMISSION_ENTITY_TYPE.
    "permissionsEntityGroup_umbricians-feature-flags": "Feature Flags",
  },
  umbriciansFeatureFlags: {
    name: "Feature Flags",
    flags: "Flags",
    intro: "Changes here apply to this site only. Developers declare flags in appsettings or code.",
    environmentTitle: "The environment you are changing flags on",
    readOnlyHeadline: "Flags can't be changed here",
    readOnlyMessage:
      "This site has switched settings from the backoffice off (Umbricians:FeatureFlags:Overrides is ReadOnly), so every flag uses its default.",

    // Permissions
    permissionView: "View feature flags",
    permissionViewDescription: "See the site's flags and who they are on for",
    permissionManage: "Change feature flags",
    permissionManageDescription: "Switch flags on and off, choose who they are for, and reset them to their defaults",
    needManage: "You need the Change feature flags permission to change this.",

    // List
    listView: "List",
    cardView: "Cards",
    columnFlag: "Flag",
    columnState: "State",
    columnSwitch: "On / off",
    columnNotes: "Notes",
    columnChanged: "Last changed",
    ungrouped: "Ungrouped",
    filterState: "State",
    filterShow: "Show",
    filterGroup: "Group",
    filterTag: "Tag",
    sort: "Sort",
    allStates: "All states",
    allGroups: "All groups",
    allTags: "All tags",
    showDeclared: "Every flag",
    showChanged: "Changed here",
    showDiffers: "Differs from default",
    showTwice: "Declared twice",
    showUndeclared: (count: number) => `Undeclared flags (${count})`,
    sortName: "Name",
    sortUpdated: "Last changed",
    stateOff: "Off",
    stateEveryone: "On for everyone",
    stateAudience: "On for an audience",
    stateCustom: "Custom",
    keeps: (audience: string) => `Keeps ${audience}`,
    changedHere: "Changed here",
    differs: "Differs from default",
    declaredTwice: "Declared twice",
    usesDefault: "Default",
    changedBy: (name: string, when: string) => `${name} · ${when}`,

    // Audience in words
    everyone: "everyone",
    nobody: "nobody yet",
    and: "and",
    members: (count: number) => plural(count, "member", "members"),
    memberGroups: (count: number) => plural(count, "member group", "member groups"),
    otherGroups: (count: number) => plural(count, "other group", "other groups"),

    // Switching
    switchOn: (name: string) => `Turn on ${name}`,
    switchOff: (name: string) => `Turn off ${name}`,
    turnOn: "Turn on",
    confirmOnHeadline: (name: string) => `Turn on ${name}?`,
    confirmOnMessage: (who: string, environment: string) =>
      `It will be on for ${who} on ${environment}, as soon as every server picks up the change.`,
    turnedOn: (name: string, environment: string) => `${name} is on on ${environment}.`,
    turnedOff: (name: string, environment: string) => `${name} is off on ${environment}.`,
    changeFailed: "The flag wasn't changed",
    remove: "Remove",
    removeHeadline: (name: string) => `Remove the setting for ${name}?`,
    removeMessage: "No flag source declares this flag any more, so its setting has no effect. Removing it can't be undone.",
    removed: (name: string) => `Removed the setting for ${name}.`,

    // Editor
    offHelp: "Nobody gets the feature. Who it was for is kept for when it is switched back on.",
    everyoneHelp: "Every visitor, signed in or not.",
    audienceHelp: "Only the members and groups chosen below.",
    customState: "This flag uses settings the backoffice can't edit yet, such as a time window or a percentage.",
    audience: "Audience",
    onFor: (who: string) => `On for ${who}.`,
    audienceMembers: "Members",
    audienceMembersHelp: "Members who get the feature.",
    audienceMemberGroups: "Member groups",
    audienceMemberGroupsHelp: "Everyone in these member groups gets the feature.",
    audienceGroups: "Other groups",
    audienceGroupsHelp: "Groups worked out by code on this site.",
    definition: "Definition",
    definitionHelp: "The flag exactly as it is stored and as Microsoft.FeatureManagement reads it.",
    definitionDefault: "The flag uses its default from where it is declared. Saving a change here stores it as an override.",
    info: "Info",
    default: "Default",
    declaredIn: "Declared in",
    declaredInCode: "Code",
    declaredInConfiguration: "Configuration",
    declaredInFlagSource: "The site's flag source",
    group: "Group",
    differsHelp: "This differs from the flag's default. Reset to default to hand it back to the developers' setting.",
    declaredTwiceHelp: "This flag is declared both in code and in configuration. Configuration's declaration is used.",
    readOnlyFlag: "This flag can't be changed here",
    unsaved: "Unsaved changes",
    save: "Save",
    saved: (name: string) => `Saved ${name}. Every server picks it up within seconds.`,
    reset: "Reset to default",
    resetHeadline: (name: string) => `Reset ${name} to its default?`,
    resetMessage: (state: string) => `It will go back to its default: ${state}.`,
    resetDone: (name: string) => `${name} is back to its default.`,
  },
} satisfies UmbLocalizationDictionary;
