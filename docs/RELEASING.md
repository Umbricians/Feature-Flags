# Releasing

For maintainers. The repository uses Gitflow: `develop` holds the next release, and `main` only changes through
release and hotfix pull requests. [`.github/workflows/build.yml`](../.github/workflows/build.yml) builds and packs
every push to `main` and `develop` and every pull request. A version tag on `main` publishes to NuGet, and so does a
pre-release tag on a `release/*` branch.

The tag sets the package version (`v17.2.3` publishes `17.2.3`, and `v17.3.0-beta.1` a pre-release). The major is the
Umbraco major the package supports, which the project's `<Version>` (`17.0.0`) carries: a tag of any other major fails
the build. The first release is `v17.0.0`; there is no 0.x or 1.0. Only the package
in `src/Umbricians.FeatureFlags` is packed; the demo site is built and smoke-tested but never published. Once the
package is on NuGet, the workflow creates a matching GitHub release with generated notes and the `.nupkg` and
`.snupkg` attached.

## A release

1. Cut the release branch from an up-to-date `develop`:

   ```bash
   git checkout develop && git pull && git checkout -b release/17.2.0
   ```

2. Push it, then open a pull request from `release/17.2.0` into `main` with the `release-flow` label. Only release
   fixes go on this branch.
3. Merge it with a merge commit (not squash) once the `build` check passes.
4. Tag the merge commit on `main` and push the tag:

   ```bash
   git checkout main && git pull && git tag -a v17.2.0 -m "v17.2.0"
   ```

   ```bash
   git push origin v17.2.0
   ```

5. In the run, open **Review deployments** and approve `production`.
6. Check the publish log says the package was pushed, not skipped as a duplicate, and that the GitHub release has
   the `.nupkg` attached. nuget.org lists the package a few minutes later, after validation.
7. Merge `main` back into `develop` through a pull request with the `release-flow` label.

## A hotfix

Cut `hotfix/17.2.1` from `main`, fix it there, and open a pull request into `main` with the `release-flow` label.
Then follow steps 3 to 7 above with the hotfix version.

## A pre-release

To publish a pre-release from a release branch before it merges, tag a commit on it with a suffix and push the tag:

```bash
git checkout release/17.2.0 && git pull && git tag -a v17.2.0-beta.1 -m "v17.2.0-beta.1"
```

```bash
git push origin v17.2.0-beta.1
```

Then follow steps 5 and 6 above. It publishes a NuGet pre-release and a GitHub pre-release, and nothing merges back.

A release tag that isn't on `main`, or a pre-release tag that isn't on a `release/*` branch, fails the build and
never publishes. If a build fails on its tag, fix it through a
hotfix and release the next version number; a version is never reused, and a tag is never moved.

nuget.org keeps the README each version was published with, so README changes reach the package page with the
next release.

## The npm types package

`@umbricians/feature-flags` (`src/Umbricians.FeatureFlags.Types`) is types only and carries the NuGet package's
version. Every build stamps that version into its `package.json` with `build/stamp-version.mjs`, type-checks it,
tests that its string literals equal the client's `constants.ts`, and packs it as the `npm-types-package` artifact.
**Nothing publishes it automatically.** To publish a release, after the NuGet package is out:

1. Download `npm-types-package` from the tag's run (its version is the tag's), and check the `.tgz` with
   `npm pack --dry-run` style listing: `index.d.ts`, `README.md`, `LICENSE`, `package.json`.
2. From a maintainer's machine logged in to an npm account in the `@umbricians` scope, run
   `npm publish umbricians-feature-flags-17.2.0.tgz --access public` (add `--tag next` for a pre-release).

Later this can move to npm trusted publishing from the `production` environment, the way NuGet's works: register the
workflow on npmjs.com, give the job `id-token: write` and run `npm publish` on the downloaded tarball. That needs the
package to exist first, so the first publish is manual either way.

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
   - a branch ruleset for `main` and `develop` that requires a pull request and the `build` status check, and
     blocks force pushes and deletion. Allow merge commits on `main`, since release and hotfix branches merge back.
   - a tag ruleset for `v*` that restricts creation, update and deletion to maintainers.
4. **Labels:** create a `release-flow` label for release, hotfix and back-merge pull requests, which
   `.github/release.yml` leaves out of the release notes.
