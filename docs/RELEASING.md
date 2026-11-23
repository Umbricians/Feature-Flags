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
7. Publish the npm types packages (see below).
8. Merge `main` back into `develop` through a pull request with the `release-flow` label.

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
4. Merge the pull request with a merge commit once the `build` check passes.
5. Tag the merge commit on `main` as `v17.0.0` and `v18.0.0`, and push both tags:

   ```bash
   git checkout main && git pull && git tag -a v17.0.0 -m "v17.0.0" && git tag -a v18.0.0 -m "v18.0.0"
   ```

   ```bash
   git push origin v17.0.0 v18.0.0
   ```

6. Approve `production` in each of the two runs, check the packages, publish the npm types packages, and merge
   `main` back into `develop`, as in steps 5 to 8 above.

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
**Nothing publishes it automatically.** To publish a release, after the NuGet packages are out, for each major:

1. Download `npm-types-package-NN` from that major's tag run, and check the `.tgz` with
   `npm pack --dry-run` style listing: `index.d.ts`, `README.md`, `LICENSE`, `package.json`.
2. From a maintainer's machine logged in to an npm account in the `@umbricians` scope, run
   `npm publish umbricians-feature-flags-17.2.0.tgz --access public --tag umbraco-17`. Use `--tag umbraco-18` and
   `--tag umbraco-19` for the others, and `--tag next` for a pre-release such as `19.0.0-rc.1`.
3. Make sure `latest` ends on the newest stable major. Publish that major last, or move the tag afterwards:
   `npm dist-tag add @umbricians/feature-flags@18.2.0 latest`. While 19 is a pre-release, the newest stable major
   is 18.

Later this can move to npm trusted publishing from the `production` environment, the way NuGet's works: register the
workflow on npmjs.com, give the job `id-token: write` and run `npm publish` on the downloaded tarball. That needs the
package to exist first, so the first publish is manual either way.

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
