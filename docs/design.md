# Design

How Feature Flags works, and why. Words in this document are used as [GLOSSARY.md](../GLOSSARY.md) defines them.
This is a working document for the people building the package: the design we are building towards, including
what comes later. Parts marked **Open** are still to decide or verify. Once the package exists, what it does is
described in the README and `docs/`, and this document keeps only the reasons.

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

A flag can carry a description, a group and tags, which help editors find and understand it. Microsoft has no place
for them: `FeatureDefinition` has no such properties, and its configuration provider drops `description`. So
Feature Flags reads them itself, from the declaration in code or from the flag's JSON in configuration:

```json
{ "id": "Checkout.ExpressPay", "enabled": false, "description": "One-click payment.",
  "group": "Checkout", "tags": ["payments", "beta"] }
```

Microsoft ignores fields it doesn't know, so these are harmless. A flag with no group takes the text before the
first `.` in its name, so `Checkout.ExpressPay` is in "Checkout" with nothing to configure. Flags from a source
Feature Flags can't read details from, such as a site's own provider, are grouped by name alone. **Open:** how to
read details for flags in Azure App Configuration, which keeps tags of its own.

**Open:** whether code declarations take a full Microsoft `FeatureDefinition` as well as the short form.

## Overrides

An editor's change is stored as an override: the flag's whole definition in Microsoft's `feature_management` JSON,
with who saved it and when. When a flag has an override, the provider returns it instead of the flag source's
default. Resetting deletes the override.

- **The default changes later:** if a deploy changes a flag's default, an existing override still wins. The
  backoffice marks flags whose override differs from their current default, so nobody is surprised.
- **The flag is removed from code:** its override becomes an undeclared flag. It has no effect, is listed
  separately, and stays until an editor removes it.
- **Precedence is a setting.** `Umbricians:FeatureFlags:Overrides` is `Enabled` (the default: overrides win) or
  `ReadOnly` (the backoffice shows flags and their state but can't change them), for teams that want their flag
  source, such as Azure App Configuration, to stay in charge. **Open:** whether a declaration can also mark a single
  flag read-only.
- **Each environment is separate.** Overrides live in each site's database, so a flag can be on in staging and off
  in production from the same deploy. They aren't moved between environments. **Open:** whether to support
  Umbraco Deploy or uSync later.

Storage is a package migration that adds one table, roughly:

| Column | |
| --- | --- |
| `Key` | `uniqueidentifier`, primary key |
| `FlagName` | `nvarchar(255)`, unique |
| `Definition` | `nvarchar(max)`: the flag in Microsoft's `feature_management` JSON |
| `UpdatedBy` | the editor's user key |
| `UpdatedUtc` | `datetime` |

Keeping the definition as Microsoft's JSON is the decision that makes later features cheap: time windows,
percentages and variants need no migration. The provider reads stored JSON with Microsoft's own
`ConfigurationFeatureDefinitionProvider`. A probe with targeting (users, groups, percentage, exclusions), a time
window, a variant and an allocation read all of them back intact. The JSON is saved as the backoffice sends it and
never rebuilt from a `FeatureDefinition`, because that direction is lossy: configuration values are all strings, so
a variant's `2` would come back as `"2"`.

**Open, to verify:** an editor's first override of a flag starts from its default, and a default from a wrapped
source arrives as a `FeatureDefinition`, not JSON. Turning that into JSON is lossless for the parts the backoffice
edits (state and audience), but not for variant values or filters it doesn't know. One option is to copy only the
parts it edits and keep the rest from the default when evaluating.

The store is an interface (`IFeatureFlagOverrideStore`), registered with
`TryAdd`, so a site can keep overrides somewhere else.

## Who the member is

Feature Flags registers an `ITargetingContextAccessor` that reads the signed-in member with Umbraco's
`IMemberManager.GetCurrentMemberAsync()`:

- **User id:** the member's key, which never changes.
- **Groups:** each of the member's member groups, by key and by name (so a developer can write a group's name in
  appsettings, and editors' picks survive a rename), plus the groups every group provider gives them.
- **Anonymous visitors:** no user id and no groups, so they are only included by *on for everyone*.

The accessor is registered so a site's own replaces it, for sites that identify people by email, by an external
id or by something other than Umbraco members.

The user id is always the member's key, with no setting to change it. Individual members are site data: the same
person has a different key in each environment's database, so choosing people belongs in the backoffice, where each
environment has its own overrides. Audiences in appsettings should name member groups and percentages, which carry
over between environments, and the docs say so. A setting for which member property is the user id would be a
trap, since editors' picks are saved by key and would silently stop matching if it changed. A site that identifies
people another way replaces the accessor.

**Open:**
- **Name clashes.** A group provider's group could have the same name as a member group. Provider groups may need a
  prefix, or a check at startup.
- **Percentages for anonymous visitors.** Microsoft's percentage rollout buckets people by user id. With no id,
  every anonymous visitor probably lands in the same bucket, so a percentage would include all of them or none.
  Check before percentages ship; a fix would give each visitor a lasting anonymous id, such as a cookie.
- **Still to check in Umbraco 17.8:** how to read a member's groups (`IMemberManager` is an ASP.NET Identity user
  manager, so likely `GetRolesAsync`), and what `umb-input-member` and `umb-input-member-group` return (keys,
  presumably).

### Group providers

```csharp
public interface IFeatureFlagGroupProvider
{
    // The groups editors can pick, shown beside member groups.
    Task<IEnumerable<FeatureFlagGroup>> GetGroupsAsync(CancellationToken cancellationToken);

    // The groups the current member is in.
    Task<IEnumerable<string>> GetGroupsForMemberAsync(IPublishedMember member, CancellationToken cancellationToken);
}
```

Several can be registered. **Open:** whether group providers are also asked about anonymous visitors (for rules
such as country or campaign), which would mean passing the request rather than a member.

## Keeping it fast

Microsoft asks the provider for a flag's definition on every check and doesn't cache it, so the provider answers
from an in-memory copy of all overrides. Saving or resetting an override updates the database, then tells every
server to reload through an Umbraco cache refresher (`JsonCacheRefresherBase` and `DistributedCache`), so
load-balanced sites stay consistent. Member groups are looked up once per request.

Deleting a member or member group (`MemberDeletedNotification`, `MemberGroupDeletedNotification`) removes it from
every override's audience.

## Registering with Microsoft.FeatureManagement

The package's composer calls `AddFeatureManagement()` and its targeting setup itself, so a site needs no code to
start, then wraps whichever `IFeatureDefinitionProvider` is registered. **Open, to verify with tests:** a site that
also calls `AddFeatureManagement()` in `Program.cs`, before or after Umbraco's composers run, must not end up with
duplicate filters or with Feature Flags bypassed. Wrapping may need to happen when the provider is resolved rather
than when it is registered.

## Backoffice

Feature Flags is one item in the Settings section's tree, which opens the flag list. It isn't one tree entry per
flag, because a site may have hundreds; Umbraco handles large lists, such as members, the same way. **Open:**
whether the tree also shows each group as a child, as a shortcut.

- **Flag list:** every declared flag with its state, a quick switch and its description where it has one;
  search by name and description; filters for state, overridden, differs from default, declared twice, undeclared,
  group and tag; sorting by name, group or last changed; a marker on flags with an
  override and on overrides that differ from their default; undeclared flags listed separately.
- **Flag editor:** state (off, on for everyone, on for an audience); members (`umb-input-member`), member groups
  (`umb-input-member-group`) and groups from group providers; who changed it and when; Reset to default. Parts of a
  definition it can't edit are shown read-only and kept.
- **Permissions:** `Umbricians.FeatureFlags.View` and `Umbricians.FeatureFlags.Manage`, granted to Administrators on
  install.

### Management API

Under `umbriciansfeatureflags/api/v1`:

| | |
| --- | --- |
| `GET flags` | every declared and undeclared flag, with default, override and state |
| `GET flags/{name}` | one flag |
| `PUT flags/{name}` | save an override (Manage) |
| `DELETE flags/{name}` | reset to default, or remove an undeclared flag (Manage) |
| `GET groups` | the groups group providers offer |

## Roadmap

1. **First release:** declared flags from any flag source and code; overrides with off, on for everyone, and on
   for members, member groups and provider groups; reset; undeclared flags; read-only mode; permissions; cache
   refresh across servers.
2. **Percentages and exclusions:** the rest of Microsoft's targeting audience.
3. **Time windows:** on from a date, off at a date, and repeating windows.
4. **Variants:** A/B options and how they are allocated.
5. **Later, if asked for:** a history of changes, headless support (telling the front end which flags are on for
   the current member), Umbraco Deploy or uSync.

## Not doing

- Creating or deleting flags in the backoffice.
- A flag-checking API of our own; site code uses Microsoft's.
- Cohorts kept by the package; member groups and group providers cover them.
