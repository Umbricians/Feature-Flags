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
for everyone. Backoffice users aren't members: being signed in to the backoffice doesn't put anyone in an audience.

If working out the member fails, the error is logged and the visitor is treated as not signed in, so a flag is never
on by mistake.

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
