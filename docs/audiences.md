# Audiences

For developers. A flag can be on for everyone, or only for an audience: chosen members, member groups, and groups the
site works out in code. This page covers how Feature Flags decides whether the current visitor is in an audience,
and how to change that.

Audiences are Microsoft's
[targeting](https://learn.microsoft.com/azure/azure-app-configuration/feature-management-dotnet-reference#targeting):
the backoffice saves a flag that is on for an audience as a `Microsoft.Targeting` filter, and Feature Flags tells
Microsoft who the visitor is.

## Who the visitor is

For each request that checks a flag, Feature Flags works out once:

- **The user id:** the signed-in member's key.
- **The groups:** each of the member's member groups, by key and by name, plus every group the site's group providers
  put them in.

A visitor who isn't signed in as a member has no user id and no groups, so they are only included when a flag is on
for everyone: Feature Flags registers its own targeting filters in place of Microsoft's (the one flags use and the one
code uses when it passes its own targeting context), under the same `Microsoft.Targeting` name. They are Microsoft's
evaluation, except that a visitor with no user id is in no percentage: not "everyone else", and not a group below 100%.
Listed members, groups at 100% (a site's own accessor may supply groups but no id) and exclusions work as in Microsoft's.
A site that registers Microsoft's targeting (`WithTargeting<T>()`) before Umbraco is added keeps its accessor and has
Microsoft's filter replaced; one that registers it after gets an error in the log at startup, since two filters with the
same name make checking an audience flag throw. Backoffice users aren't members: being signed in to the backoffice doesn't put anyone in an audience.

If working out the member fails, the error is logged and the visitor is treated as not signed in, so a flag is never
on by mistake.

## Percentages and exclusions

An audience can include more than named people:

- **A percentage of everyone else.** "…and 25% of everyone else" includes that share of signed-in members who aren't
  already in the audience.
- **A percentage of a group.** "50% of this group" includes half of the group's members. A group is in full at 100%.
- **Exclusions.** "Except these members or groups" are off for the flag even when they are in a chosen group or in a
  percentage. Exclusions win.

Percentages are Microsoft's, and they are sticky: Microsoft hashes each member's id together with the flag's name, so
a member who is in a 25% audience stays in it, and the same share of members is in on every server. Raising the
percentage only adds members; nobody who had the feature loses it. Changing the flag's name reshuffles who is in.

Percentages count **members only**: signed-in members have an id to hash. A visitor who isn't signed in has none.
Microsoft would treat all of them alike, in or out together, so Feature Flags never counts them: a visitor with no user id
is left out of every percentage, of everyone else or of a group below 100%. Listed members and groups at 100% still
apply. Feature Flags sets no cookie to give visitors ids.

To roll a flag out to visitors who aren't Umbraco members, give them ids with your own `ITargetingContextAccessor`
(see [Identifying people another way](#identifying-people-another-way)). For example, with a visitor id kept in a
cookie your site already sets:

```csharp
using Microsoft.FeatureManagement.FeatureFilters;

public class VisitorIdTargetingContextAccessor(IHttpContextAccessor http) : ITargetingContextAccessor
{
    public ValueTask<TargetingContext> GetContextAsync()
    {
        string? id = http.HttpContext?.Request.Cookies["visitor-id"];
        return new(new TargetingContext { UserId = id, Groups = [] });
    }
}
```

Percentages then apply to those ids. Register the accessor as shown below.

In appsettings, the same settings are `DefaultRolloutPercentage`, each group's `RolloutPercentage` and
`Exclusion`. Percentages are whole numbers from 0 to 100; the backoffice refuses a flag saved with anything else. A
group listed without a `RolloutPercentage` includes nobody, as Microsoft reads it.

```json
"Audience": {
  "Users": [],
  "Groups": [{ "Name": "Beta testers", "RolloutPercentage": 50 }],
  "DefaultRolloutPercentage": 10,
  "Exclusion": { "Users": [], "Groups": ["Staff"] }
}
```

When a member or member group is deleted, Feature Flags removes it from exclusions as well as from the audience.

## Audiences in appsettings

A flag can be declared on for an audience. Name member groups rather than members: a member's key is different in
each environment's database, but a group's name is the same everywhere.

```json
{
  "id": "Search.BetaSearch",
  "enabled": true,
  "conditions": {
    "client_filters": [
      {
        "name": "Microsoft.Targeting",
        "parameters": {
          "Audience": {
            "Groups": [{ "Name": "Beta testers", "RolloutPercentage": 100 }],
            "DefaultRolloutPercentage": 0
          }
        }
      }
    ]
  }
}
```

Choose individual members in each environment's backoffice instead. The backoffice saves member groups by key, so
picks survive a group being renamed.

A member group named by name in appsettings shows in the editor under **Member groups**, marked "named in
configuration", and is kept when an editor saves the flag.

When a member or member group is deleted, Feature Flags removes it from every saved audience.

## Group providers

A group provider puts members in groups by a rule of the site's own: everyone who joined in the last 30 days,
premium customers in a CRM, staff by email address. Editors pick its groups in the flag editor, under **Other
groups**, beside member groups.

```csharp
using Umbricians.FeatureFlags.Groups;

public class ExampleDomainGroupProvider : IFeatureFlagGroupProvider
{
    private const string GroupId = "demo:example-com";

    public Task<IEnumerable<FeatureFlagGroup>> GetGroupsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<FeatureFlagGroup>>(
            [new FeatureFlagGroup(GroupId, "example.com members", "Members whose email address is at example.com.")]);

    public Task<IEnumerable<string>> GetGroupIdsForMemberAsync(FeatureFlagMember member, CancellationToken cancellationToken) =>
        Task.FromResult<IEnumerable<string>>(
            member.Email?.EndsWith("@example.com", StringComparison.OrdinalIgnoreCase) == true ? [GroupId] : []);
}
```

Register it:

```csharp
builder.AddFeatureFlags(flags => flags.AddGroupProvider<ExampleDomainGroupProvider>());
```

- `GetGroupsAsync` returns the groups editors can pick. Each id is what flags store, so it must be unique across every
  provider and never clash with a member group's name or key. Prefix ids with something of the provider's own, such
  as `crm:premium`, and don't change them once editors have used them.
- `GetGroupIdsForMemberAsync` returns the ids of the groups a member is in. `FeatureFlagMember` gives the member's key,
  username, email and member group names.
- A provider is a singleton, and is asked about the signed-in member on each request that checks a flag. Keep it
  fast, and cache anything slow to look up, such as a call to a CRM.
- A site can register several.

Provider groups can also be named in appsettings audiences by their id.

## Identifying people another way

For sites whose visitors aren't Umbraco members, such as people signed in with an external identity provider,
register your own Microsoft `ITargetingContextAccessor`. It replaces the package's, whether it is registered before or
after Feature Flags:

```csharp
builder.Services.AddSingleton<ITargetingContextAccessor, ExternalIdTargetingContextAccessor>();
```

The backoffice's member picker still saves member keys, so with your own accessor, audiences are best built from
groups, using ids your accessor returns.
