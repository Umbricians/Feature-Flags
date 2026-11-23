# Releasing

For maintainers. The repository uses Gitflow: `develop` holds the next release, and `main` only changes through
release and hotfix pull requests. [`.github/workflows/build.yml`](../.github/workflows/build.yml) builds and packs
every push to `main` and `develop` and every pull request. A version tag on `main` publishes to NuGet, and so does a
pre-release tag on a `release/*` branch.

## One source, one package per Umbraco major

One source builds a separate NuGet package (and npm types package) for each Umbraco major the package supports:
17, 18 and 19. The package's major is the Umbraco major, so `17.2.3` is for Umbraco 17 and `18.2.3` for Umbraco 18.
`-p:UmbracoMajor=18` chooses the Umbraco version, the .NET version and the build folders (see
[`Directory.Build.props`](../Directory.Build.props)); the default is 17.

On a pull request or a push to `main` or `develop`, the workflow builds all three majors as three jobs, named
`build (Umbraco 17)`, `build (Umbraco 18)` and `build (Umbraco 19)`. A job named `build` follows them and fails
unless all three passed. That aggregate `build` is the required status check, so the rulesets never name a major.

A tag builds and publishes only its own major: `v18.2.3` publishes `18.2.3` and runs only the Umbraco 18 job. A tag
of any other major fails the build. A pre-release tag such as `v19.0.0-rc.1` publishes a pre-release. Only the package
in `src/Umbricians.FeatureFlags` is packed; the demo site is built and smoke-tested but never published. Once a
package is on NuGet, the run creates a matching GitHub release with generated notes and the `.nupkg` and `.snupkg`
attached. The notes start at the previous stable release of the same major.

## A release

One release is one tag per supported major, all on the same commit and with the same minor and patch: `v17.2.0`,
`v18.2.0` and `v19.2.0`. Each tag is a separate run and needs its own approval of `production`. The release branch is
named after its 17.x version.

1. Cut the release branch from an up-to-date `develop`:

   ```bash
   git checkout develop && git pull && git checkout -b release/17.2.0
   ```

2. Push it, then open a pull request from `release/17.2.0` into `main` with the `release-flow` label. Only release
   fixes go on this branch.
3. Merge it with a merge commit (not squash) once the `build` check passes.
4. Tag the merge commit on `main` once per major, and push the tags:

   ```bash
   git checkout main && git pull
   for major in 17 18 19; do git tag -a "v$major.2.0" -m "v$major.2.0"; done
   ```

   ```bash
   git push origin v17.2.0 v18.2.0 v19.2.0
   ```

   Pushing several tags at once may start only some of the runs. Check there is one run per tag, and push any
   missing tag again on its own.
5. In each run, open **Review deployments** and approve `production`.
6. Check each publish log says the package was pushed, not skipped as a duplicate, and that each GitHub release has
   its `.nupkg` attached. nuget.org lists the packages a few minutes later, after validation.
7. Check each run also published the npm types package, or publish it by hand while npm publishing is off (see
   below).
8. Merge `main` back into `develop` through a pull request with the `release-flow` label.
9. In that back-merge or the next pull request, move each released major's `PackageValidationBaselineVersion` in
   `Directory.Build.props` to the version just released. Packing fails when a major's public API breaks against its
   baseline, so breaking changes wait for a new Umbraco major.

Umbraco 19 stays a pre-release until Umbraco 19.0.0 is out (due 10 December 2026), so until then leave `v19.x.y`
out of step 4 and tag it as `v19.N.P-rc.M` on the release branch, as in the first release below.

## The first release

The first release is `17.0.0`. There is no 0.x or 1.0. Until Umbraco 19.0.0 is out, `UmbracoVersion` for 19 in
`Directory.Build.props` is `19.0.0-rc1`, and its package is a pre-release.

1. Cut `release/17.0.0` from `develop`, push it and open the pull request into `main` as above. Don't merge it yet.
2. Tag the head of `release/17.0.0` as `v19.0.0-rc.1` and push the tag. A pre-release tag must be on a `release/*`
   branch, which is why it is tagged before the merge: after the merge the commit is on `main` too, but the
   pre-release must come from the release branch.

   ```bash
   git checkout release/17.0.0 && git pull && git tag -a v19.0.0-rc.1 -m "v19.0.0-rc.1"
   ```

   ```bash
   git push origin v19.0.0-rc.1
   ```

3. Approve `production` in its run, and check the NuGet pre-release and the GitHub pre-release.
4. Publish `@umbricians/feature-flags@19.0.0-rc.1` by hand, the one npm publish that can't come from the workflow,
   and switch npm publishing on, as in "The first npm publish" below. Tags pushed after that publish to npm
   themselves.
5. Merge the pull request with a merge commit once the `build` check passes.
6. Tag the merge commit on `main` as `v17.0.0` and `v18.0.0`, and push both tags:

   ```bash
   git checkout main && git pull && git tag -a v17.0.0 -m "v17.0.0" && git tag -a v18.0.0 -m "v18.0.0"
   ```

   ```bash
   git push origin v17.0.0 v18.0.0
   ```

7. Approve `production` in each of the two runs, check the packages on NuGet and npm, and merge `main` back into
   `develop`, as in steps 5 to 8 above. Push `v17.0.0` before `v18.0.0` is fine either way: npm's `latest` ends on
   18.

## Umbraco 19.0.0

When Umbraco 19.0.0 is out:

1. In `Directory.Build.props`, change `UmbracoVersion` for 19 from `19.0.0-rc1` to `19.0.0`. Release it through
   `develop` like any change.
2. Tag `v19.0.0` on `main` with the release's other tags, as in a release. It is a stable release, so it must be
   on `main`. Its notes start at the previous stable `v19.` release, if there is one.

## A hotfix

Cut `hotfix/17.2.1` from `main`, fix it there, and open a pull request into `main` with the `release-flow` label.
Then follow steps 3 to 8 above with the hotfix version, tagging every major the fix applies to (a fix that only
affects one major can release just that major's tag).

## A pre-release

To publish a pre-release from a release branch before it merges, tag a commit on it with a suffix and push the tag.
It builds only that tag's major:

```bash
git checkout release/17.2.0 && git pull && git tag -a v18.2.0-beta.1 -m "v18.2.0-beta.1"
```

```bash
git push origin v18.2.0-beta.1
```

Then follow steps 5 and 6 above. It publishes a NuGet pre-release and a GitHub pre-release, and nothing merges back.

A release tag that isn't on `main`, or a pre-release tag that isn't on a `release/*` branch, fails the build and
never publishes. If a build fails on its tag, fix it through a
hotfix and release the next version number; a version is never reused, and a tag is never moved.

nuget.org keeps the README each version was published with, so README changes reach the package page with the
next release.

## The npm types package

`@umbricians/feature-flags` (`src/Umbricians.FeatureFlags.Types`) is types only. Each major has its own version,
which is the NuGet package's, and its peer dependency on `@umbraco-cms/backoffice` is `^` that major's lowest
Umbraco version. Every build stamps both into its `package.json` with `build/stamp-types.mjs`, installs the
major's own backoffice types, type-checks, tests that its string literals equal the client's `constants.ts`, and
packs it as the `npm-types-package-17`, `npm-types-package-18` or `npm-types-package-19` artifact. Each major's
`.tgz` comes from its own tag's run.

**Publishing.** Once switched on, the `publish` job publishes the tag's `.tgz` to npm after NuGet, in the same
`production` approval. It uses npm trusted publishing, so no npm token is stored. The dist-tag:

- a pre-release, such as `19.0.0-rc.1`, gets `next`;
- a release of the newest stable major gets `latest`, so `npm install @umbricians/feature-flags` takes it;
- a release of an older major gets `umbraco-NN`, such as `umbraco-17`, and never takes `latest` from a newer major.

Install the major that matches your Umbraco with `npm install -D @umbricians/feature-flags@18`. A version that is
already on npm is skipped, so a run can be re-run.

### The first npm publish

npm only trusts a workflow for a package that already exists, so the first version is published by hand, once:

1. Download `npm-types-package-19` from the `v19.0.0-rc.1` run, and check the `.tgz` holds `index.d.ts`,
   `README.md`, `LICENSE` and `package.json`.
2. From a machine logged in to an npm account that can publish to the `@umbricians` scope, run
   `npm publish umbricians-feature-flags-19.0.0-rc.1.tgz --access public --tag next`.
3. On npmjs.com, open the package's **Settings → Trusted publishing**, choose GitHub Actions, and enter owner
   `Umbricians`, repository `Feature-Flags`, workflow `build.yml` and environment `production`. npm doesn't check
   these until the next publish, so check the spelling.
4. In the package's settings, set publishing access to require two-factor authentication and disallow tokens, so
   only the workflow and a maintainer can publish.
5. In the GitHub repository, **Settings → Secrets and variables → Actions → Variables**, add the variable
   `NPM_PUBLISH` with the value `true`.

If a run published to NuGet but not npm (npm publishing was off, or npm failed), re-run its `publish` job, or
publish that run's `.tgz` by hand with the dist-tag above.

## Adding an Umbraco major

When Umbraco 20 comes, add it in four places:

1. `Directory.Build.props`: one more `PropertyGroup` with its lowest `UmbracoVersion`, target framework and ASP.NET
   Core version.
2. The `plan` job in `build.yml`: one more entry with its major, .NET SDK and API document path.
3. The matrix in `umbraco-minors.yml`: one more entry with its major and .NET SDK.
4. This document: the list of majors above, and the tags in the release steps.

Drop a major the same way when it is no longer supported.

## How publishing is authorised

Publishing uses [NuGet Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so no
API key is stored anywhere. The workflow exchanges GitHub's OIDC token for a NuGet API key that lasts one hour.
nuget.org only accepts that token from this repository, this workflow file and the `production` environment. The
environment only lets `v*` tags deploy and waits for a maintainer's approval, so a branch, a pull request or a fork
can't publish.

## One-off setup

Make the repository public before the first release, unless the Umbricians organization is on GitHub Team or
higher. On GitHub Free a private repository has no rulesets and silently ignores the environment rules below, so
nothing would stop a branch from publishing. The package page also needs it public: the NuGet README's images and
links point at this repository, and the Umbraco Marketplace reads `umbraco-marketplace.json` from it. Check the
GitHub settings below again after making it public.

**On nuget.org**, signed in as a member of the `umbricians` organization, create a trusted publishing policy with
owner `umbricians`, repository owner `Umbricians`, repository `Feature-Flags`, workflow `build.yml` and environment
`production`. Owner and repository are matched without regard to case.

**In the GitHub repository**, every item is required:

1. **Branches:** push `main` and `develop`, then make `develop` the default branch (**Settings → General**).
2. **Settings → Environments:** create an environment named `production`, and in it:
   - add **required reviewers** (the maintainers) and leave **Prevent self-review** off, so a maintainer working
     alone can approve their own release. Turn off **Allow administrators to bypass configured protection rules**,
     so every publish still waits for an approval in the run;
   - under **Deployment branches and tags**, choose **Selected branches and tags** and add one rule of type **Tag**
     for `v*`, with no branch rules;
   - add a secret `NUGET_USER` holding the nuget.org **username** (profile name, not email) of the account that
     **created** the trusted publishing policy. For a policy owned by an organization, that is still the creator's
     own username.
3. **Settings → Rules → Rulesets:**
   - a branch ruleset for `main` and `develop` that requires a pull request and the `build` status check (the aggregate job; the per-major jobs are not required by name), and
     blocks force pushes and deletion. Allow merge commits on `main`, since release and hotfix branches merge back.
   - a tag ruleset for `v*` that restricts creation, update and deletion to maintainers.
4. **Labels:** create a `release-flow` label for release, hotfix and back-merge pull requests, which
   `.github/release.yml` leaves out of the release notes.
