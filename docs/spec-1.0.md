# 1.0 spec

What Feature Flags 1.0 adds to the first release, so developers can extend it and build on it, and what it
promises to keep stable. Words are used as [GLOSSARY.md](../GLOSSARY.md) defines them. Each part links to the issue
that decided it, which holds the reasoning; the map is
[Map: extensibility and missing features for 1.0](https://github.com/Umbricians/Feature-Flags/issues/6).

Until 1.0 ships nothing is promised, so 1.0 changes public shapes freely and keeps no compatibility with 0.x. From
1.0, everything listed under [What 1.0 promises](#what-10-promises) changes in a breaking way only in a new major
version.

## Contents

1. [Settings and conditions](#settings-and-conditions)
2. [The package's own conditions](#the-packages-own-conditions)
3. [Changing overrides from code](#changing-overrides-from-code)
4. [Notifications](#notifications)
5. [Management API](#management-api)
6. [Headless](#headless)
7. [Backoffice extension surface](#backoffice-extension-surface)
8. [Health check](#health-check)
9. [Testing with flags](#testing-with-flags)
10. [What 1.0 promises](#what-10-promises)
11. [Not in 1.0](#not-in-10)
12. [Build order](#build-order)
13. [To check first](#to-check-first)
14. [Docs to write](#docs-to-write)

## Settings and conditions

From [Editing a developer's own conditions in the backoffice](https://github.com/Umbricians/Feature-Flags/issues/9).

An override's settings are on or off plus a list of conditions, which replaces today's on/off plus an audience:

```csharp
public sealed record FlagSettings(bool Enabled, IReadOnlyList<FlagCondition> Conditions, ConditionMatch Match = ConditionMatch.Any);
public sealed record FlagCondition(string Name, JsonObject Parameters);   // Name is the Microsoft filter's name
public enum ConditionMatch { Any, All }
```

- The audience is the built-in `Microsoft.Targeting` condition. `settings.Audience` reads it and
  `FlagSettings.ForAudience(audience)` builds it. `FlagAudience` is public.
- **State:** *off*; *on for everyone* (no conditions); *on for an audience* (only the audience); *on with conditions*
  (any other registered conditions); *custom* (a condition nobody registered, or variants: shown, never changed).
- With two or more conditions, editors choose **Any** or **All**. The default is Any, as Microsoft's. When an editor
  adds the [Root condition](#root-only-on-these-sites) to a flag that already has another condition, the backoffice
  switches to All and says so; editors can change it back.
- Stored as today: the flag's whole definition in Microsoft's JSON. The table doesn't change.

### A developer's own condition

A condition has two halves. The developer registers the Microsoft filter that evaluates it the normal Microsoft way,
then:

**C#:** `flags.AddCondition<T>()` registers

```csharp
public interface IFeatureFlagCondition
{
    string Name { get; }                                   // the Microsoft filter it edits
    string DisplayName { get; }
    IEnumerable<string> Validate(JsonObject parameters);   // problems to show the editor; none means valid
    string Describe(JsonObject parameters);                // a one-line summary for the list
}
```

Parameters are validated on every save, whether from the API or from `IFeatureFlagService`.

**Backoffice:** a manifest of the new type `umbriciansFeatureFlagsCondition`, whose `meta.name` matches the C#
`Name`. Its element takes the parameters as JSON in `value` and fires `change`. The flag editor gets an "Add
condition" menu listing every registered condition.

**Only one half registered:** with only the C# half, the list shows the summary, but the flag can't be changed ("this
condition has no editor"). With only the backoffice half, it is ignored and the condition is treated as unknown.

## The package's own conditions

Built on the same contract as developers' conditions. From
[Which Roadmap items are in 1.0](https://github.com/Umbricians/Feature-Flags/issues/10) and
[Flags for one site of several](https://github.com/Umbricians/Feature-Flags/issues/16).

### Audience (`Microsoft.Targeting`)

Today's audience editor becomes the first `umbriciansFeatureFlagsCondition`, and gains the rest of Microsoft's
targeting audience:

- "…and N% of everyone else", and "N% of this group" for each chosen group;
- "except these members or groups".

Percentages are Microsoft's, sticky for each id, and include **members only**: the editor and the docs say so. The
package sets no cookie for visitors; the docs show how a developer gives visitors ids with their own
`ITargetingContextAccessor`.

### Time window (`Microsoft.TimeWindow`)

"On from … until …", with either end optional. Editors enter and see times in their browser's time zone, with UTC
shown beside it; times are stored as UTC.

A window with a recurrence shows its summary but can't be changed ("repeating windows can't be edited yet"), reported
through the condition's `Validate`.

### Root: "Only on these sites"

The package's own condition for a site with several [roots](../GLOSSARY.md#root).

- Editors pick roots from the Content tree. They are stored by key, not by domain, and each root is shown with its
  domains.
- **Which root a request is in:**
  1. the root of the content the request found;
  2. if no content, the `Start-Item` header (a root's key or path), as the Delivery API uses it;
  3. if no header, the root of the domain matching the request's host and path;
  4. otherwise no root, and the condition fails.
- **A deleted root** stays in the condition by key, is summarised as "a deleted site" and never matches. The
  [health check](#health-check) warns. There's no automatic clean-up.
- One override per flag: settings aren't saved separately for each root.

Cultures ("on for French only") aren't in 1.0. The docs use a culture condition as the worked example of a
developer's own condition.

## Changing overrides from code

From [A public service for changing overrides from code](https://github.com/Umbricians/Feature-Flags/issues/8) and
[Forcing flags in developers' tests](https://github.com/Umbricians/Feature-Flags/issues/14).

```csharp
public interface IFeatureFlagService
{
    Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<FeatureFlag?> GetAsync(string name, CancellationToken cancellationToken = default);
    Task<FeatureFlagResult> SaveAsync(string name, FlagSettings settings, Guid? userKey = null, CancellationToken cancellationToken = default);
    Task<FeatureFlagResult> SwitchAsync(string name, bool on, Guid? userKey = null, CancellationToken cancellationToken = default);
    Task<FeatureFlagResult> ResetAsync(string name, Guid? userKey = null, CancellationToken cancellationToken = default);
}
```

- **`FeatureFlag`**, the public read model, is what the backoffice sees: the name; whether, where and whether twice
  it is declared; section, description, tags and whether it is [public](#headless); its state and default; its
  override (settings, who, when); whether that differs from the default; and its actions (switch, save, reset,
  remove), each allowed or not, with a reason. Undeclared flags are included. There's no filtering or paging;
  developers use LINQ.
- **Rules** are the backoffice's (read-only mode, undeclared flags can't be saved, custom flags can't be switched,
  audience groups normalised, conditions validated), with **no permission checks**, because code isn't a backoffice
  user.
- **Who:** the optional `userKey` credits a backoffice user. Calls through this interface always have the cause
  `Code`; only the package's Management API sends `Editor`, through an internal path.
- **`FeatureFlagResult`** is `Saved`, `NotFound`, `NotAllowed` (with the reason) or `Cancelled` (with the
  notification handlers' messages). Exceptions are only for real failures.
- **Seen at once:** when a save, switch or reset returns, the next check on the same server sees it. Other servers
  see it within seconds, as today.
- No saving many flags in one call; the docs show seeding as a loop.
- `IFeatureFlagOverrideStore` stays public only for keeping overrides somewhere else. The docs say to change
  overrides through `IFeatureFlagService`, because writing to the store skips the rules, the notifications and the
  refresh on other servers.

## Notifications

From [Notifications when overrides change](https://github.com/Umbricians/Feature-Flags/issues/7).

| Notification | When | Cancellable |
| --- | --- | --- |
| `FeatureFlagOverrideSavingNotification` | before an override is saved: an editor's save or switch, or code's | yes |
| `FeatureFlagOverrideSavedNotification` | after it is saved, and after this server has read the overrides again | no |
| `FeatureFlagOverrideResettingNotification` | before an override is reset; removing an undeclared flag's override counts | yes |
| `FeatureFlagOverrideResetNotification` | after the reset, and after this server has read the overrides again | no |

- **Each carries** the flag's name; its settings before and after (`null` for no override); whether it is declared;
  the user's key (`null` from code with no user); when; and the **cause**: `Editor`, `Code` or `MemberCleanup`.
- Reset records who did it (today only save does).
- **Cancelling** uses Umbraco's event messages. The change isn't made, `IFeatureFlagService` returns `Cancelled`
  with the messages, and the Management API returns them to the editor.
- **Member clean-up** (taking a deleted member or group out of every audience) publishes only the Saved notification,
  with the cause `MemberCleanup`, and can't be cancelled.
- **Servers:** notifications fire only on the server that made the change, like Umbraco's. Code that must react on
  every server handles the Feature Flags cache refresher's notification; the docs name it.
- No note or reason on a change in 1.0.

## Management API

From [The Management API as a public contract](https://github.com/Umbricians/Feature-Flags/issues/13).

- **Callers:** the backoffice, and scripts or CI pipelines signed in as an Umbraco **API user** (client credentials)
  in a user group with the Feature Flags permissions. No API keys or sign-in of the package's own.
- **A thin layer** over `IFeatureFlagService`. Results map to `Saved` → 200, `NotFound` → 404, `NotAllowed` → 409
  with the reason, and `Cancelled` → 409 with the messages.
- **Cause** is `Editor` with the user's key, for editors and API users alike.
- **Shapes mirror the C# types.** The save body is `{ enabled, conditions: [{ name, parameters }], match }`, and a
  flag is `FeatureFlag`. The `{ enabled, audience }` body is dropped. The [npm types package](#publishing) describes
  the same shapes.
- **Last save wins.** No `ETag` or `If-Match` in 1.0; they can be added later without breaking anything.
- **No bulk endpoint:** scripts call `PUT flag` once per flag.
- **Versioning:** the `v1` in the path follows the package's major version. A breaking change ships in 2.0 as
  `/api/v2/`, and 2.0 drops `v1`.

## Headless

From [Telling a headless front end which flags are on](https://github.com/Umbricians/Feature-Flags/issues/17).

- **Endpoint:** `GET /umbraco/umbriciansfeatureflags/delivery/v1/flags`, in its own Swagger document. It's **off
  until a developer switches it on** in the package's settings, and it follows the Delivery API's access settings:
  when the Delivery API isn't public, it needs the same `Api-Key`.
- **Only public flags.** A developer marks a flag public with `public: true` in `Declare(...)`, or `"public": true`
  beside `section` and `tags` in appsettings. Flags are private by default. The flag list shows which flags are
  public; editors can't change it.
- **Returns** every public flag and whether it's on for this request, for example
  `{ "Checkout.ExpressPay": true, "Search.Beta": false }`. No audiences, conditions or variants.
- **Who the member is:** the package's member lookup recognises members signed in with the Delivery API's member
  tokens as well as the member cookie, for every request, not only this endpoint.
- **Root:** the `Start-Item` header, as in the [Root condition](#root-only-on-these-sites).
- **Caching:** always `Cache-Control: private, no-store`.
- Flags aren't added to Delivery API content responses, which are cached and the same for everyone.

## Backoffice extension surface

From [The backoffice extension surface 1.0 promises](https://github.com/Umbricians/Feature-Flags/issues/12), with
research in [What Umbraco 17 packages publish for backoffice extension](https://github.com/Umbricians/Feature-Flags/issues/11).

**Promised:**

- the entity types `umbricians-feature-flags-flag` and `umbricians-feature-flags-root`;
- the aliases of the flag workspace, the root workspace, the collection and the menu item;
- the permission condition's alias and config, so a developer's UI can show only to editors who may view or change
  flags;
- the `umbriciansFeatureFlagsCondition` manifest type and its element's contract (`value` in, `change` out);
- a new read-only flag workspace context, `UMBRICIANS_FEATURE_FLAG_WORKSPACE_CONTEXT`, with a `flag` observable
  shaped like `FeatureFlag`; changes go through conditions or the Management API, never the context;
- entity actions developers register for the flag entity, shown in the flag workspace's actions menu and on each row
  of the list (the row half is dropped if it proves costly).

**Not promised:** element tag names, repositories, the generated API client, the editor's state module, the summary
context, and the built-in collection and workspace views. No column extension type: a developer who needs a
different list adds a collection view to the promised collection.

### Publishing

A types-only npm package, `@umbricians/feature-flags`, with the NuGet package's version and `@umbraco-cms/backoffice`
^17 as a peer dependency, as Forms and Commerce publish theirs. It has no runtime code and no import-map entry. All
the aliases and entity types above are exported from it as constants.

## Health check

From [A health check for flags](https://github.com/Umbricians/Feature-Flags/issues/15).

An Umbraco health check in its own "Feature Flags" group, sharing the startup checks' code so the log and the
dashboard agree.

| Check | Result |
| --- | --- |
| Saved settings can't be read (the store is failing, or the migration hasn't run) | Error |
| Microsoft.FeatureManagement is registered twice | Error |
| The site's flag source is registered after Feature Flags, so the backoffice has no effect | Error (and an error in the startup log, which is a warning today) |
| One flag's saved setting can't be read, so it uses its default | Warning |
| A declared flag's definition can't be read | Warning |
| The flag source can't list its flags | Warning |
| A group provider fails to list its groups | Warning |
| A flag is declared both in code and by the flag source | Warning |
| A Root condition refers to a deleted root | Warning |
| Undeclared flags are waiting to be removed | Info, with a count |
| Overrides are read-only | Info |
| Flags whose state is custom | Info, with a count |
| Everything is fine | Success |

- Errors and warnings name the flags. Info results only count them, because the dashboard is open to users who may
  not have View feature flags.
- No buttons. Each result links to its explanation in the docs.
- No ASP.NET Core `IHealthCheck`, and developers don't add results to this group.

## Testing with flags

From [Forcing flags in developers' tests](https://github.com/Umbricians/Feature-Flags/issues/14). No test package;
the docs cover it:

- **Unit tests** mock Microsoft's `IFeatureManager` or `IVariantFeatureManager`.
- **Integration tests**, two ways:
  - set `Umbricians:FeatureFlags:Overrides` to `ReadOnly` and declare the flags in an in-memory `feature_management`
    section (the default);
  - or change flags through `IFeatureFlagService`, which goes through the real path.
- **As a particular member:** sign in as a test member, or register a fixed `ITargetingContextAccessor`.

## What 1.0 promises

Stable for all of 1.x; a breaking change needs 2.0.

| Area | Promised |
| --- | --- |
| C# | `IFeatureFlagService`, `FeatureFlag`, `FeatureFlagResult`, `FlagSettings`, `FlagCondition`, `ConditionMatch`, `FlagAudience`, `IFeatureFlagCondition`, the four notifications and the cause, `IFeatureFlagGroupProvider`, `IFeatureFlagOverrideStore`, `AddFeatureFlags` and its builder, the settings under `Umbricians:FeatureFlags` |
| Behaviour | a save is seen by the next check on the same server; notifications fire as above |
| Management API | every `v1` endpoint, field, query parameter and status code. Adding endpoints, fields or parameters doesn't break it. Not human-readable text, and not the order of items except where `orderBy` sets it. |
| Headless | the endpoint, its response shape and the `public` declaration |
| Backoffice | the list under [Backoffice extension surface](#backoffice-extension-surface) |

## Not in 1.0

After 1.0, if asked for:

- variants: flags with variants stay custom, and keep working in code;
- repeating time windows;
- ids for visitors who aren't signed in;
- conditions by culture;
- a test package, or a public in-memory store;
- an ASP.NET Core health check;
- columns in the flag list;
- `ETag` / `If-Match`, and saving several flags at once;
- a note or reason on a change.

Out of scope, with what covers the need instead:

- **Permissions per section:** a Saving handler refuses the change (a docs example).
- **A history of changes:** a Saved/Reset handler writes to Umbraco's audit log (a docs example). A history screen
  can follow 1.0.
- **Umbraco Deploy or uSync:** overrides belong to each environment; scripts copy them through the Management API. A
  connector would be its own package.

## Build order

Each step builds on the ones before it.

1. **Public types and settings:** `FlagSettings` with conditions, `FlagCondition`, `ConditionMatch`, `FlagAudience`,
   `FeatureFlag`, `FeatureFlagResult`, and the `public` declaration.
2. **`IFeatureFlagService`**, over today's override service, with "seen at once" covered by a test.
3. **Notifications**, published by the service, with cancelling, the cause and member clean-up.
4. **The condition contract:** `IFeatureFlagCondition`, `AddCondition<T>()`, validation on save, and the
   *on with conditions* and *custom* states.
5. **Management API** on the service, with the new shapes and the result mapping.
6. **The backoffice condition editor:** the `umbriciansFeatureFlagsCondition` type, "Add condition", Any/All, and the
   audience editor moved onto it.
7. **The package's conditions:** the audience's percentages and exclusions, the time window, then Root.
8. **Health check**, sharing the startup checks.
9. **Headless:** member tokens in the member lookup, `Start-Item`, then the endpoint.
10. **Backoffice extension surface:** the workspace context, entity actions, constants, and the npm types package.
11. **Docs** (below), and the README.

## To check first

Facts the decisions assume but nobody has checked yet:

- Whether Umbraco's `IMemberManager.GetCurrentMemberAsync()` sees a member signed in with a Delivery API member
  token. If not, the member lookup reads the token itself.
- Whether the package's permission check passes for an Umbraco API user signed in with client credentials.
- Whether Umbraco's promise of no breaking changes in minor releases covers the backoffice TypeScript the promised
  surface depends on.

## Docs to write

| Page | What |
| --- | --- |
| docs/declaring-flags.md | `public`; "Testing with flags" with a WebApplicationFactory example |
| docs/conditions.md (new) | writing your own condition, with a culture condition as the example; the package's own conditions |
| docs/audiences.md | percentages (members only), exclusions, ids for visitors with your own accessor |
| docs/backoffice.md | "Extending the backoffice": the promised list and the npm package |
| docs/api.md (new) | "Calling the API from scripts": API users, client credentials, `curl`, what's promised; the README's API table moves here |
| docs/headless.md (new) | switching it on, public flags, member tokens, `Start-Item`, a `fetch` example |
| docs/notifications.md (new) | the four notifications, cancelling, the cache refresher for every server; the section-permission and audit-log examples |
| docs/getting-started.md | run the health check before going live, with each result explained |
