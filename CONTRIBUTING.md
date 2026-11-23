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

Run the steps the `build` check runs before opening a pull request. These build Umbraco 17, the default:

```bash
cd src/Umbricians.FeatureFlags/Client && npm ci && npm test && npm run build && cd -
dotnet build Umbricians.FeatureFlags.slnx -c Release
dotnet test Umbricians.FeatureFlags.slnx -c Release --no-build
node build/smoke-test.mjs demo/Umbricians.FeatureFlags.Demo /App_Plugins/UmbriciansFeatureFlags/umbricians-feature-flags.js /umbraco/swagger/umbricians-feature-flags/swagger.json
dotnet pack src/Umbricians.FeatureFlags -c Release -o artifacts
```

Each Umbraco major is its own package, built from the same source. Build and test one major at a time with
`-p:UmbracoMajor=18` or `-p:UmbracoMajor=19` on the `dotnet` commands. Each major has its own folders under `bin` and
`obj`, such as `bin/umbraco18/`. Umbraco 17 and 18 run on .NET 10; 19 needs the .NET 11 SDK. For 18, the smoke test's
API document is `/umbraco/openapi/umbricians-feature-flags.json`, and it takes the major as a last argument:

```bash
node build/smoke-test.mjs demo/Umbricians.FeatureFlags.Demo /App_Plugins/UmbriciansFeatureFlags/umbricians-feature-flags.js /umbraco/openapi/umbricians-feature-flags.json 18
```

The smoke test checks the demo serves the backoffice, the package's bundle and its API document. Run it after building
the same major, and pass `19` for Umbraco 19.

### Coverage

Coverage of the package assembly, `Umbricians.FeatureFlags`, must stay at or above 90%. Measure it with the unit tests:

```bash
dotnet test tests/Umbricians.FeatureFlags.Tests --collect "Code Coverage;Format=cobertura"
```

The run prints the path of a `*.cobertura.xml` file under `tests/Umbricians.FeatureFlags.Tests/TestResults/`. In it,
read the `line-rate` attribute of `<package name="Umbricians.FeatureFlags">`: `0.977` is 97.7%. The file also has a
package for the test assembly and a total at the top, which are not the number to watch. `TestResults` is git-ignored.

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
