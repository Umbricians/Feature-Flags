<img src="assets/logo.svg" alt="" width="96" height="96">

# Umbricians Feature Flags

[![NuGet](https://img.shields.io/nuget/v/Umbricians.FeatureFlags?label=NuGet)](https://www.nuget.org/packages/Umbricians.FeatureFlags)
[![Build](https://github.com/Umbricians/Feature-Flags/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/Umbricians/Feature-Flags/actions/workflows/build.yml)

Switch the site's feature flags on and off from the Umbraco backoffice, for everyone or only for chosen members and member groups, built on Microsoft.FeatureManagement. (Umbraco 17.8.0+)

<!-- What it does for a site, in a few sentences, then a screenshot from the demo site in docs/images/. -->

## Documentation

- **[Getting started](docs/getting-started.md)**: install, first use, configuration, and what to check before going
  live.

## Installation

```bash
dotnet add package Umbricians.FeatureFlags
```

Restart the site. No configuration is needed to get started.

## Permissions

<!-- The package's user group permissions, if it has any: a table of permission and what it allows, and which groups
get them on install. Remove this section if the package adds none. -->

<!-- The package's own topics go here, one ## section each. -->

## Configuration

Everything is optional. Override only what you need in `appsettings.json`:

```json
{
  "Umbricians": {
    "FeatureFlags": {
    }
  }
}
```

<!-- Every setting, with its default. -->

## Extending

<!-- The documented extension points: interfaces a site implements or calls, and the AddFeatureFlags… builder methods,
each with a short example. Remove this section if there are none. -->

## API

<!-- The package's Management API endpoints under /umbraco/umbriciansfeatureflags/api/v1/ and who can call each.
Remove this section if there are none. -->

## Licence

[MIT](LICENSE). Copyright (c) 2026 FYIN LLC (trading as Umbricians).

## Development

```
src/Umbricians.FeatureFlags          the package (Razor class library + Client/ TypeScript)
demo/Umbricians.FeatureFlags.Demo    Umbraco 17.8.0 site on SQLite that references the package
tests/Umbricians.FeatureFlags.Tests  NUnit tests, run with `dotnet test`
```

```bash
cd src/Umbricians.FeatureFlags/Client && npm ci && npm run build
```

```bash
dotnet run --project demo/Umbricians.FeatureFlags.Demo
```

Run `npm run watch` in `Client/` while working on the backoffice, and `npm test` there for its tests. The demo's
test admin login is in `demo/Umbricians.FeatureFlags.Demo/appsettings.Development.json`.

> The demo reads `appsettings.{environment}.json`. If your machine sets `DOTNET_ENVIRONMENT` (for example to
> `Local`), that overrides the launch profile's `Development`, so the demo settings also live in
> `appsettings.Local.json`.

To package, build the client first, then:

```bash
dotnet pack src/Umbricians.FeatureFlags -c Release
```

Packing stops with an error if the backoffice bundle has not been built. Pack with a build, as above: packing with
`--no-build` after building the bundle leaves its files out of the package.

Branches follow Gitflow; see [CONTRIBUTING.md](CONTRIBUTING.md). Release steps for maintainers are in
[docs/RELEASING.md](docs/RELEASING.md).
