# Design

How Feature Flags works, and why. Words in this document are used as [GLOSSARY.md](../GLOSSARY.md) defines them.
It is for the people building the package: the reasons behind the design, and what comes later. What the package
does, and how to use it, is in the [README](../README.md) and the other pages in `docs/`. Parts that come after the first
release are in the [Later](#later) list.

## What it is

A backoffice for [Microsoft.FeatureManagement](https://learn.microsoft.com/azure/azure-app-configuration/feature-management-dotnet-reference).
Developers declare flags and check them with Microsoft's own API. Editors switch them on and off in Umbraco, for
everyone or for an audience of members and groups, on each site and environment separately.

Feature Flags doesn't check flags, define its own flag format or replace the site's flag source. Anything a site can
do with Microsoft.FeatureManagement keeps working, and code that checks flags doesn't know Feature Flags is there.

## Principles

1. **Use Microsoft's model, not our own.** Flags, states, audiences, time windows and variants are Microsoft's
   types, stored as Microsoft's JSON. A new Microsoft feature should need a new screen, not a new data model.
2. **Wrap the site's flag source.** Sites already declare flags in appsettings, environment variables, Azure App
   Configuration or their own provider. Feature Flags adds overrides on top of whichever they use.
3. **Developers declare, editors switch.** Only code can make a flag do something, so only flag sources declare
   flags. Editors can't create or delete them, and deploying a new flag never needs a backoffice step.
4. **Never lose what the backoffice doesn't understand.** A flag whose definition uses filters or variants the
   backoffice has no screen for is shown read-only, and saving never drops them.
5. **Every moving part is replaceable.** Who the current member is, what groups they are in, where overrides are
   stored and which source wins are interfaces or settings a site can change, registered so the site's version
   wins whichever composer runs first.
6. **Safe by default.** New flags are declared off. Unknown flags are off. An anonymous visitor is in no audience.
7. **Fast on every request.** Flags are checked many times per page, so evaluation never touches the database.

## How it fits together

```
 site code: IFeatureManager.IsEnabledAsync("NewCheckout"), [FeatureGate], <feature>
        │
        ▼
 Microsoft FeatureManager ──► filters (targeting, time window, percentage, …)
        │                          │
        │                          └─► ITargetingContextAccessor ── Feature Flags: current member's
        │                                                            key and groups
        ▼
 IFeatureDefinitionProvider ── Feature Flags' provider:
        │                       override from memory if there is one, else ─┐
        │                                                                   ▼
        │                                          the site's own flag source (appsettings,
        │                                          Azure App Configuration, custom provider)
        ▼
 in-memory overrides ◄── reloaded on every server when an editor saves (Umbraco cache refresher)
        ▲
 database (overrides) ◄── Management API ◄── backoffice
```

## Declaring flags

Developers declare flags in any flag source Microsoft supports. appsettings, in either of Microsoft's schemas:

```json
"feature_management": {
  "feature_flags": [
    { "id": "NewCheckout", "enabled": false, "description": "The redesigned checkout." }
  ]
}
```

or in code, for sites that would rather not use configuration:

```csharp
builder.AddFeatureFlags(flags => flags
    .Declare("NewCheckout", description: "The redesigned checkout.")
    .AddGroupProvider<PremiumSubscribers>());
```

`AddFeatureFlags` is the package's one builder method; everything a site adds goes through it. This departs on
purpose from the conventions' `Add{Package}{Thing}` pattern, which would give `AddFeatureFlagsFlag(…)` and
`AddFeatureFlagsGroupProvider<T>()`. A flag declared in code is off unless its declaration says otherwise.

When code and configuration declare the same flag, configuration wins, and the package logs a warning at startup
naming the flag. The backoffice marks it too. This is the same order as the package's own settings, where
appsettings wins over code. It also gives whoever runs the servers a way to turn a broken feature off with an
environment variable and a restart, without a deploy or the backoffice. A flag declared in both places is usually a
mistake, which is why it warns.

### Details for the backoffice

A flag can carry a description, a section and tags, which help editors find and understand it. Microsoft has no place
for them: `FeatureDefinition` has no such properties, and its configuration provider drops `description`. So
Feature Flags reads them itself, from the declaration in code or from the flag's JSON in configuration:

```json
{ "id": "Checkout.ExpressPay", "enabled": false, "description": "One-click payment.",
  "section": "Checkout", "tags": ["payments", "beta"] }
```

Microsoft ignores fields it doesn't know, so these are harmless. A flag with no section takes the text before the
first `.` in its name, so `Checkout.ExpressPay` is in "Checkout" with nothing to configure. Flags from a source
Feature Flags can't read details from, such as a site's own provider, are put in sections by name alone. Reading details
for flags in Azure App Configuration, which keeps tags of its own, comes after 1.0.

Microsoft's configuration provider also drops the filters of a flag configured with `"enabled": false`, so Feature
Flags reads the filters of a disabled flag from the `feature_management:feature_flags` list itself; switching it on
then keeps them. For any other flag source (its own provider, Azure App Configuration) the filters of a disabled flag
can't be known, so such a flag shows as off with no conditions, and switching it on saves "on for everyone". Declare
it enabled, with its conditions, if it matters.

Code declarations take a full Microsoft `FeatureDefinition` as well as the short form, so code can declare anything
configuration can.

## Overrides

An editor's change is stored as an override: the flag's whole definition in Microsoft's `feature_management` JSON,
with who saved it and when. When a flag has an override, the provider returns it instead of the flag source's
default. Resetting deletes the override. A flag source that can't list its flags still has each flag read on its own,
so the backoffice shows every flag it can read.

- **The default changes later:** if a deploy changes a flag's default, an existing override still wins. The
  backoffice marks flags whose override differs from their current default, so nobody is surprised.
- **The flag is removed from code:** its override becomes an undeclared flag. It has no effect, is listed
  separately, and stays until an editor removes it.
- **Precedence is a setting.** `Umbricians:FeatureFlags:Overrides` is `Enabled` (the default: overrides win) or
  `ReadOnly` (the backoffice shows flags and their state but can't change them), for teams that want their flag
  source, such as Azure App Configuration, to stay in charge. Marking a single flag read-only in its declaration
  comes after 1.0.
- **Each environment is separate.** Overrides live in each site's database, so a flag can be on in staging and off
  in production from the same deploy. They aren't moved between environments; scripts can copy them through the
  Management API, and a Deploy or uSync connector would be a package of its own.

Storage is a package migration that adds one table, `umbriciansFeatureFlagsOverride`:

| Column | |
| --- | --- |
| `id` | `int`, primary key |
| `flagName` | `nvarchar(255)`, unique |
| `definition` | `nvarchar(max)`: the flag in Microsoft's `feature_management` JSON |
| `updatedBy` | the editor's user key |
| `updatedUtc` | `datetime` |

The migration, and saving overrides through Umbraco's scopes, are tested against SQL Server as well as SQLite, with a
test site started in the tests. Two saves of a flag with no override can both try to add its row at once; the unique
index refuses the second, which then updates the first's row instead.

Keeping the definition as Microsoft's JSON is the decision that makes later features cheap: time windows,
percentages and variants need no migration. The provider reads stored JSON with Microsoft's own
`ConfigurationFeatureDefinitionProvider`. A probe with targeting (users, groups, percentage, exclusions), a time
window, a variant and an allocation read all of them back intact. The JSON is saved as the backoffice sends it and
never rebuilt from a `FeatureDefinition`, because that direction is lossy: configuration values are all strings, so
a variant's `2` would come back as `"2"`.

A default from a wrapped source arrives as a `FeatureDefinition`, not JSON, and turning that into JSON is lossy for
variant values and filters the backoffice doesn't know. So the backoffice only edits flags whose definition it fully
understands (off, on for everyone, on for an audience of users and groups at 100%), and writes their JSON itself.
Any other flag is **Custom** and read-only, so nothing is ever rebuilt from a lossy copy.

A flag's settings are whether it is on, and its conditions: Microsoft's filters (`conditions.client_filters`), with
`requirement_type` saying whether any or all of them must hold. An audience is one targeting condition; the backoffice
reads and writes every filter, keeping the parameters of those it doesn't edit exactly as stored.

Microsoft's parser drops a disabled flag's conditions, so a flag switched off would lose its audience. The backoffice
reads its own overrides' JSON directly, so turning a flag off keeps who it was for.

The store is an interface (`IFeatureFlagOverrideStore`), registered with
`TryAdd`, so a site can keep overrides somewhere else.

## Who the member is

Feature Flags registers an `ITargetingContextAccessor` that reads the signed-in member with Umbraco's
`IMemberManager.GetCurrentMemberAsync()`:

- **User id:** the member's key, which never changes.
- **Groups:** each of the member's member groups, by key and by name (so a developer can write a group's name in
  appsettings, and editors' picks survive a rename), plus the groups every group provider gives them.
- **Anonymous visitors:** no user id and no groups, so they are only included by *on for everyone*.

Targeting keeps every group id in one list, so `AudienceGroups` tells the editor what each id is: a member group by
key, a member group named in appsettings, a group a provider offers, or unknown (the key of a deleted member group,
or a provider group no longer offered). The backoffice shows each kind where it belongs, keeps named and unknown
groups when an editor saves, and lets the editor remove them. It reads the site's member groups and providers when
asked and never caches them, since member groups change while the site runs. A provider that fails to list its
groups is logged and offers none for that request. When a member group's key is saved, it is written the way
targeting compares keys; every other id is kept as it was written.

The accessor is registered so a site's own replaces it, for sites that identify people by email, by an external
id or by something other than Umbraco members.

The user id is always the member's key, with no setting to change it. Individual members are site data: the same
person has a different key in each environment's database, so choosing people belongs in the backoffice, where each
environment has its own overrides. Audiences in appsettings should name member groups and percentages, which carry
over between environments, and the docs say so. A setting for which member property is the user id would be a
trap, since editors' picks are saved by key and would silently stop matching if it changed. A site that identifies
people another way replaces the accessor.

- **Name clashes.** A group provider's group could have the same name as a member group. Provider ids win in the
  editor: an id a provider offers is shown as the provider's group even when a member group has that key. A prefix
  for provider groups, or a check at startup, comes later.
- **Percentages for anonymous visitors.** Microsoft's percentage rollout buckets people by user id, and a visitor
  who isn't signed in has none, so percentages include members only and the editor says so. The package sets
  no cookie; a site that wants visitors included gives them ids with its own accessor.

### Group providers

`IFeatureFlagGroupProvider` offers groups editors can pick (`GetGroupsAsync`) and says which of them a member is in
(`GetGroupIdsForMemberAsync`). It is given a small `FeatureFlagMember` record (key, username, email, member group
names) rather than Umbraco's member types, so a provider doesn't depend on how Umbraco loads members, and the
record can grow without breaking providers. Several can be registered. They are asked only about members; asking them about visitors who aren't signed in
(for rules such as country or campaign) comes later.

## Keeping it fast

Microsoft asks the provider for a flag's definition on every check and doesn't cache it, so the provider answers
from an in-memory copy of all overrides. Each server reads them when the site starts. Saving or resetting an
override updates the database, then tells every server to read them again through an Umbraco cache refresher
(`CacheRefresherBase` and `DistributedCache.RefreshAll`), so load-balanced sites stay consistent. A server reads
them in the background and keeps answering from the copy it has until the new one is ready, so a flag check never
waits on the database; only the server that saved the change waits for its own read, so the editor sees it at once.
If a read fails, the last good copy stays in use and the read is tried again in the background. The one exception
is a check that arrives before a server's first read has finished, which waits for it rather than answer with
defaults that might switch on something an editor switched off. Each override's JSON is read once per reload, into what Microsoft evaluates and what the backoffice shows. Member groups are looked up once per request.

A JSON cache refresher isn't needed: every change means "read them all again", so there is no payload to send.

Deleting a member or member group (`MemberDeletedNotification`, `MemberGroupDeletedNotification`) removes it from
every override's audience.

## Registering with Microsoft.FeatureManagement

The package's composer calls `AddFeatureManagement()` and its targeting setup itself, so a site needs no code to
start, then wraps whichever `IFeatureDefinitionProvider` is registered. It only calls `AddFeatureManagement()` if the
site hasn't: calling it twice registers Microsoft's time window filter twice, and checking a flag that uses it then
throws. A site that calls it, or registers its own flag source, after Umbraco is added can't be wrapped at
registration, so the package checks at startup and logs an error or warning saying what to move. Tests cover each
order.

## Backoffice

Feature Flags is one item in the Settings section's tree, which opens the flag list. It isn't one tree entry per
flag, because a site may have hundreds; Umbraco handles large lists, such as members, the same way. Showing each
section as a child in the tree, as a shortcut, comes later.

- **Flag list:** every declared flag with its state, a quick switch and its description where it has one;
  search by name and description; filters for state (including *custom*, for flags the backoffice can't edit),
  section and tag; checkboxes for changed here, differs from default and declared twice, which combine; sorting by
  section (the default, so each section appears once) or by last changed; a marker on flags with an override and on
  overrides that differ from their default; undeclared flags as a separate list. It shows as a list or as cards,
  the editor's choice, and is paged (25 a page by default), with search, filters and sorting applied across every
  page.
- **Which site:** the list and the confirmation before switching a flag on name the environment, such as
  Production, since each environment has its own settings and an editor may have several open.
- **Flag editor:** state (off, on for everyone, on for an audience); members (`umb-input-member`), member groups
  (`umb-input-member-group`) and groups from group providers; who changed it and when; Reset to default. Parts of a
  definition it can't edit are shown read-only and kept.
- **Permissions:** `Umbricians.FeatureFlags.View` and `Umbricians.FeatureFlags.Manage`, granted to Administrators on
  install.

### Built from Umbraco's own parts

The screens use Umbraco's components, so they look and behave like the rest of the backoffice; nothing is drawn by
hand that Umbraco already has. Checked in Umbraco 17.8, which ships UUI 2.2.1:

| Part | Umbraco component |
| --- | --- |
| The list, its toolbar, search, paging and list/cards switch | the collection framework: a `collection` with two `collectionView` extensions (list and cards), `umb-collection-toolbar`, `umb-collection-filter-field`, `umb-collection-pagination`, `umb-collection-view-bundle` |
| List view | `uui-table` |
| Card view | `uui-card` |
| On/off switch | `uui-toggle` |
| State and markers | `uui-tag` |
| Paging | `uui-pagination`, through `umb-collection-pagination` |
| Editing screen | a workspace, with `uui-tabs`, `uui-box`, `uui-radio`, `uui-checkbox`, `uui-button` |
| Member and member group pickers | `umb-input-member`, `umb-input-member-group` |
| Confirming a switch-on | Umbraco's confirm modal |
| Messages after a change | Umbraco's notifications (`uui-toast-notification`) |

The collection gets its items from a `repository` extension that calls the package's API, and the Settings tree's
menu item opens a workspace whose only view is the collection.

### One package per Umbraco major

The same source builds a package for each Umbraco major, chosen with the `UmbracoMajor` build setting (17 by default).
Where a major needs different code, the source uses `#if UMBRACO_18_OR_GREATER`, as the API document does: Umbraco 18
replaced Swashbuckle with Microsoft.AspNetCore.OpenApi.

### Management API

The endpoints are listed in the [README](../README.md#api). A flag's name is a query parameter (`flag?name=`), not a
path segment, because names contain `.` and may contain `/` or `:`. Searching, filtering and sorting happen on the
server, across every flag, so paging stays correct on a site with hundreds.

## Roadmap

Both parts below ship together in the first release, for Umbraco 17, 18 and 19: 17.0.0, 18.0.0 and 19.0.0-rc.1. The
package's major follows Umbraco's, one package per major from one source, so 17.x is for Umbraco 17, 18.x for 18 and
19.x for 19.

1. **The core:** declared flags from any flag source and code; overrides with off, on for everyone, and on for
   members, member groups and provider groups; reset; undeclared flags; read-only mode; permissions; cache refresh
   across servers.
2. **What developers can extend and rely on** (planned as "1.0"): conditions of their own, editable in the
   backoffice; percentages and exclusions in the audience; time windows; the Root condition for a site with several
   roots; a public service and notifications for changing overrides; the Management API as a contract; a health
   check; flags for headless front ends; and a promised backoffice extension surface, with its optional npm types.
   Specified in [spec-1.0.md](spec-1.0.md).

### Later

If asked for: variants; repeating time windows; ids for visitors who aren't signed in; conditions by culture; a
test package; an ASP.NET Core health check; columns in the flag list; `ETag` / `If-Match` and saving several flags
at once; a note on a change, and a history screen; a connector for Umbraco Deploy or uSync; reading details from Azure App Configuration; marking a single
flag read-only in its declaration; a prefix or startup check for provider groups named like member groups; asking
group providers about visitors who aren't signed in; sections as shortcuts in the Settings tree.

## Not doing

- Creating or deleting flags in the backoffice.
- A flag-checking API of our own; site code uses Microsoft's.
- Cohorts kept by the package; member groups and group providers cover them.
