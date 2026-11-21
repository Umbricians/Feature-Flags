# Contributing

How the Umbricians team works on Umbricians Feature Flags. Contributions come from the team; whether outside
contributions are accepted is decided when the repository is made public, and this page says so then.

## Branches

The repository uses Gitflow:

- `develop` is the default branch and holds the next release.
- Work happens on a `feature/…` branch cut from `develop`, and goes back to `develop` through a pull request.
- `main` only changes through a `release/x.y.z` or `hotfix/x.y.z` pull request, and every version is tagged there.

Open your pull request against `develop`. Larger changes start as an issue saying what and why, with "Done when"
criteria the pull request is checked against.

## Building and testing

Run the steps the `build` check runs before opening a pull request:

```bash
cd src/Umbricians.FeatureFlags/Client && npm ci && npm test && npm run build && cd -
dotnet build Umbricians.FeatureFlags.slnx -c Release
dotnet test Umbricians.FeatureFlags.slnx -c Release --no-build
node build/smoke-test.mjs demo/Umbricians.FeatureFlags.Demo /App_Plugins/UmbriciansFeatureFlags/umbricians-feature-flags.js umbricians-feature-flags
dotnet pack src/Umbricians.FeatureFlags -c Release -o artifacts
```

### SQL Server tests

The unit tests run against SQLite. The integration tests in `tests/Umbricians.FeatureFlags.IntegrationTests` start a
real Umbraco site, `tests/Umbricians.FeatureFlags.TestSite`, against a new database on SQL Server, so the migration,
Umbraco's scopes and saves that happen at the same moment run as they do on a live site. They need Docker:

```bash
build/sql-server.sh
```

This starts a SQL Server container called `featureflags-sql` on port 14330, or reuses it, and writes its connection
string to `tests/sqlserver.local.env`, which is git-ignored. The tests read that file, or the `FEATUREFLAGS_SQLSERVER`
environment variable if you would rather point them at a server of your own. Each run makes a database called
`FeatureFlagsTests_…` and drops it afterwards. Without either, every integration test is reported as ignored, so
`dotnet test` still passes without Docker; the `build` check does not run them yet.

## Pull requests

Fill in the template: a summary of what changed, the evidence it works, and the merge danger. Link the issue it
closes with "Closes #N".
