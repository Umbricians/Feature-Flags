# 1.0 build plan

How [spec-1.0.md](spec-1.0.md) is built: twelve slices, one stacked PR each, every branch cut from the one before.
Delete this file when 1.0 ships.

**Status:** all twelve slices are built, and a polish pass followed.

**Every slice leaves the solution green:**

- 0 build warnings (CS1591 is an error);
- `dotnet test` passes, and the SQL Server integration tests pass when `tests/sqlserver.local.env` is present;
- `npm test` and `npm run build` pass in `src/Umbricians.FeatureFlags/Client`;
- coverage of the package assembly stays at 90% or more (see [Coverage](#coverage)).

New types are `internal` unless the spec (or the [public types](#public-types) below) names them, are records where
they fit, and have XML docs in plain English using [GLOSSARY.md](../GLOSSARY.md) terms. Client logic lives in modules
with no backoffice imports, like `flag-editor-state.ts`, so `npm test` covers it.

## Choices made while planning

The spec left these open; the build follows the choice given here. They are also listed for review in each slice's
PR.

### Public types

`FeatureFlag` and the notifications need more public types than the spec names: `FeatureFlagState`,
`FeatureFlagDeclaredIn`, `FeatureFlagActions` and `FeatureFlagAction`, `FeatureFlagChangeCause`, and the existing
`FeatureFlagOverride` for who changed a flag and when. They are promised with the rest. The Feature Flags cache
refresher's notification becomes public too, because the docs tell developers to handle it to react on every server.

In C#, `public` is a keyword, so `Declare(...)` takes `isPublic:`. Configuration uses `"public": true`.

### Other choices

| | Choice |
| --- | --- |
| A flag on the wire | `FeatureFlag`'s fields, plus display-only fields added for the backoffice (the updater's name, each audience group's kind and name). `GET flags` stays paged, with `FeatureFlag` items. |
| Conditions from a flag source's default | Their parameters arrive as configuration strings, so they become a `JsonObject` of strings. Conditions accept numbers written as strings; the package's own conditions write proper types when saved. |
| The new state | `conditions` in the API, `Conditions` in the enum. |
| Failed validation | `NotAllowed` (409), with the problems joined in the reason and listed in a `problems` field. |
| Root's filter | `Umbricians.Root`, with parameters `{ "Roots": ["<key>", …] }`. |
| A condition with no backoffice editor | The server can't know, so it allows saving; the backoffice disables it and says why. |
| Short filter names | `Targeting` and `TimeWindow` match their `Microsoft.` names, as in Microsoft's own matching. |
| Settings before and after | `FlagSettings?`, null when there was no override or it can't be read as settings; the stored JSON is carried as well. |
| Who reset a flag | The row is deleted, so who did it is carried only by the Reset notifications. |
| Time windows | Stored as ISO 8601 UTC ending in `Z`, which Microsoft parses. |
| Headless with the Delivery API switched off | Served whenever `Headless:Enabled` is on, applying the Delivery API's `PublicAccess` and `ApiKey` settings either way. |
| The npm package | Its own folder, `src/Umbricians.FeatureFlags.Types`, versioned with the NuGet package. CI packs it; publishing is in RELEASING.md. |
| The backoffice promise | Umbraco's no-breaking-minors policy doesn't cover its backoffice TypeScript ([checks](research/1.0-checks.md)), so ours is promised within one Umbraco major, with CI against new Umbraco minors. |

### Coverage

No tool measured coverage before 1.0. Slice 1 adds `dotnet test --collect "Code Coverage;Format=cobertura"`
(Microsoft.CodeCoverage comes with the test SDK), measured on the package assembly, to CONTRIBUTING.md.

## Slices

### 1. Settings with conditions, and public flags

`feature/1.0-settings-and-public-flags`. Spec: Settings and conditions; the `public` declaration.

Replace on/off plus audience with on/off plus conditions, and let developers mark flags public, without changing the
API or the backoffice yet.

- `Flags/FlagSettings.cs`: public `FlagSettings`, `FlagCondition`, `ConditionMatch`; `FlagAudience` public, with
  `settings.Audience` and `FlagSettings.ForAudience`. `SameAs` compares conditions by deep JSON equality.
- `Flags/FlagDefinitions.cs`: write `conditions.client_filters` and `requirement_type`; read every filter into
  conditions, keeping parameters as stored. Variants still mean not editable.
- The state gains `Conditions`; for now anything but one targeting filter stays custom (slice 4 decides by what is
  registered).
- `ManageFlagsController`, `FlagResponses` and member clean-up adapt through `Audience`/`ForAudience`, so the wire
  shape is unchanged.
- `FlagDetails` gains `IsPublic`: `Declare(..., isPublic:)`, `"public": true` in configuration, and
  `MakePublic(params string[] names)` on the builder.
- Coverage measurement in CONTRIBUTING.md.

Accept: round trips for Any/All, several filters, unknown filters kept exactly, an audience kept while off; every
existing test passes; `public` read from code, both appsettings schemas and `MakePublic`.
Docs: declaring-flags.md (`public`), design.md (settings are conditions).

### 2. A public service for changing flags from code

`feature/1.0-flag-service`. Spec: Changing overrides from code; Testing with flags.

- Public `IFeatureFlagService`; internal `FeatureFlagService` replaces `OverrideService`.
- Public `FeatureFlag`, `FeatureFlagResult` and the [public types](#public-types). `FlagCatalog` builds
  `FeatureFlag` directly, retiring `CatalogFlag`; the list pages over `FeatureFlag`.
- An internal save path with a cause, for the controllers. `ResetAsync` takes a user key.

Accept: a unit test per rule (read-only, undeclared, custom, normalised groups, not found); "seen at once": a save
is seen by `IFeatureManager.IsEnabledAsync` straight after; an integration test saves and resets through the
service on SQL Server.
Docs: "Testing with flags" in declaring-flags.md; the README's extension table.

### 3. Notifications when overrides change

`feature/1.0-notifications`. Spec: Notifications.

- The four public notifications, on Umbraco's cancelable and stateful notifications with event messages, carrying
  name, settings before and after, declared, user key, when and cause; `FeatureFlagChangeCause`.
- Published by the service; Saved and Reset after the cache reloads; cancelling returns `Cancelled`.
- Member clean-up publishes only Saved, with `MemberCleanup`. The cache refresher notification becomes public.

Accept: order (Saving, store, reload, Saved); a cancelled change writes nothing and returns the messages; cause
`Code` through the interface, `Editor` through the internal path; clean-up fires only Saved; integration: a handler
in the test site cancels a save.
Docs: docs/notifications.md (new), with the section-permission and audit-log examples.

### 4. The condition contract

`feature/1.0-condition-contract`. Spec: A developer's own condition; State.

- Public `IFeatureFlagCondition` and `AddCondition<T>()`; an internal registry matching names Microsoft's way.
- The audience registered as the package's own condition through the same contract.
- State: no conditions, everyone; only the audience, audience; all registered and no variants, conditions;
  otherwise custom.
- Every save validates; problems mean `NotAllowed`. `Describe` gives `FeatureFlag` a summary.

Accept: a test per state; a developer's condition validates and describes; an unregistered filter is custom and
can't be switched; validation runs from the service and the API.
Docs: docs/conditions.md (new), with a culture condition as the example.

### 5. The Management API as a contract

`feature/1.0-management-api`. Spec: Management API.

First, check an API user (client credentials) in a group with both permissions can call `GET flags` on the test
site; if `BackOfficeSecurity.CurrentUser` is null for API users, find the user from the principal's key.

- Controllers become a thin layer over `IFeatureFlagService`; responses mirror `FeatureFlag`.
- `PUT flag` takes `{ enabled, conditions, match }`; the audience body is gone.
- 200, 404, 409 with the reason, 409 with `messages` when cancelled; `state=conditions`.
- The backoffice keeps working: `api/types.ts`, the resource, and `savePayload` writing a targeting condition.

Accept: a test per status code; a cancelled save returns its messages; an API user drives the API in an integration
test; `npm test` covers `savePayload`.
Docs: docs/api.md (new); the README's API table moves there.

### 6. The backoffice condition editor

`feature/1.0-condition-editor`. Spec: the backoffice half of a developer's condition; Any/All.

- The `umbriciansFeatureFlagsCondition` manifest type, declared in `UmbExtensionManifestMap`.
- The audience editor moves into `conditions/audience/`, taking `value` and firing `change`.
- "Add condition"; Any/All for two or more conditions, switching to All when Root is added, and saying so.
- A condition with no editor shows its summary and can't be saved.

Accept: client tests for adding and removing conditions, Any/All, the switch to All, no editor, and unsaved changes;
the manifest test lists the new manifest.
Docs: backoffice.md.

### 7. Percentages and exclusions in the audience

`feature/1.0-audience-percentages`. Spec: Audience.

- Read and write the default and per-group percentages and exclusions; validate 0–100; member clean-up also takes
  ids out of exclusions.
- The editor: "…and N% of everyone else", "N% of this group", "except these members or groups", members only.

Accept: round trips, validation, clean-up of exclusions; client tests for the audience state.
Docs: audiences.md.

### 8. The time window condition

`feature/1.0-time-window`. Spec: Time window.

- `Microsoft.TimeWindow` as the package's condition: dates must parse, start before end; a recurrence is shown but
  refused ("repeating windows can't be edited yet"); a UTC summary.
- The editor converts between the browser's time and UTC, showing both.

Accept: either end optional; start after end refused; recurrence not editable; on inside the window and off outside;
client tests for the conversions.
Docs: conditions.md.

### 9. The Root condition

`feature/1.0-root-condition`. Spec: Root.

- Finding a request's root: content, else `Start-Item`, else domain, else none.
- A `Umbricians.Root` filter, and its condition: "a deleted site" for a missing root.
- `GET roots?keys=` with names, domains and whether deleted; a root picker in the editor.

Accept: each step of the order; a deleted root never matches; integration with two roots by host and by
`Start-Item`; adding Root switches to All.
Docs: conditions.md. If it runs long, the picker moves to its own PR.

### 10. The health check

`feature/1.0-health-check`. Spec: Health check.

- One place works out every row of the spec's table; the startup log and the health check both use it. A flag
  source registered after Feature Flags is now an error.
- The override cache keeps its last read error and which overrides failed.
- An Umbraco health check in the "Feature Flags" group; errors and warnings name flags, info counts; each result
  links to the docs.

Accept: a test per row and for success; the log and the health check agree.
Docs: getting-started.md, with an anchor for each result.

### 11. Flags for headless front ends

`feature/1.0-headless`. Spec: Headless.

First, check whether `IMemberManager` sees a member signed in with a Delivery API token; if not, the member lookup
reads the token itself ([checks](research/1.0-checks.md)).

- Member tokens recognised on every request.
- `Umbricians:FeatureFlags:Headless:Enabled`, off by default; the endpoint answers 404 while off.
- `GET umbriciansfeatureflags/delivery/v1/flags` in its own Swagger document, following the Delivery API's access
  rules; public flags only, as `{ name: bool }`; `Cache-Control: private, no-store`; root from `Start-Item`.

Accept: off is 404; private flags left out; the `Api-Key` required when the Delivery API isn't public; the cache
header; a member token's member is targeted; `Start-Item` limits by root.
Docs: docs/headless.md (new).

### 12. The backoffice extension surface and npm types

`feature/1.0-extension-surface`. Spec: Backoffice extension surface; Publishing.

- The read-only flag workspace context under a documented alias; entity actions in the workspace's menu and on each
  list row.
- `src/Umbricians.FeatureFlags.Types`: `@umbricians/feature-flags`, types only, with string literal types for the
  promised strings and the API's shapes.
- CI against new Umbraco minors.

Accept: a test that the literal types match `constants.ts`; the types package compiles; a test entity action shows
in both places.
Docs: "Extending the backoffice" in backoffice.md; the README; design.md; RELEASING.md (npm).
