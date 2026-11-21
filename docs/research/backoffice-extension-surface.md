# What Umbraco 17 packages publish for backoffice extension

Research for issue #11 (child of map #6). It feeds the decision "The backoffice extension surface 1.0 promises" (#12).

Researched 2026-11-21. Primary sources only: docs.umbraco.com (17.latest pages), the published npm tarballs (downloaded and
read), and the Umbraco-CMS and uSync GitHub repos. Version facts are for Umbraco 17 unless marked.

## Summary

- Umbraco's extension system is open. A package adds **manifest types** by declaring a TypeScript interface and merging it into the global
  `UmbExtensionManifestMap`; it adds **condition configs** by merging into `UmbExtensionConditionConfigMap`. Umbraco documents both.
  Umbraco documents no convention for "kinds a package offers" beyond the generic `kind` type.
- Umbraco does not document a convention for entity types, context tokens or element names a package should publish. Umbraco's own code uses
  exported string constants (`UMB_DOCUMENT_ENTITY_TYPE = "document"`) and `UmbContextToken` constants; add-ons follow the same shape with their
  own prefix (`forms-datasource`, `uc:order`).
- The official add-ons (Forms, Commerce, Deploy, Workflow, Engage) publish a **types-only npm package** `@umbraco-<product>/backoffice`
  whose major version matches the product and whose peer dependency is `@umbraco-cms/backoffice`. The runtime stays in the NuGet package.
- The one community package found that publishes **runtime** client code to npm is uSync (`@jumoo/usync`). Its child packages mark it
  `external` in Vite and uSync supplies an `importmap` entry so every child shares the one loaded copy.
- Umbraco documents no npm-publishing guidance for third parties. Everything below the "Documented" headings is from the docs; everything
  under "Observed" is read from package contents and is convention, not contract.

## What Umbraco documents (17.latest)

### Package file and registration

Source: https://docs.umbraco.com/umbraco-cms/17.latest/extend-your-project/backoffice-extensions/umbraco-package.md

- `umbraco-package.json` lives in `App_Plugins/{Name}/` (scanned two levels deep), or in `wwwroot` of a Razor class library with
  `StaticWebAssetBasePath` mapped to `App_Plugins/{Name}`.
- Root fields: `id`, `name`, `version`, `allowTelemetry`, `allowPublicAccess`, `allowCacheBusting`, `importmap`, `extensions`.
- `importmap` takes `imports` and `scopes` (MDN import-map spec). The docs' own example lets a package expose a module so
  consumers write `import { MyService } from 'mypackage/services'`. This is the documented mechanism for a package to expose runtime code to
  other packages.
- Manifests can come from JSON (`extensions`), from a `bundle` extension whose `js` re-exports `UmbExtensionManifest[]`
  (https://docs.umbraco.com/umbraco-cms/17.latest/extend-your-project/backoffice-extensions/extending-overview/extension-types/bundle.md),
  or from C# via `IPackageManifestReader` (replaces the JSON file; remove the file to avoid double registration).
- Cache busting from **17.6**: Umbraco appends `?umb__rnd={version}` to `/App_Plugins` assets and import map entries, using the
  manifest `version`. It does not touch URLs that already have a query string, URLs outside `/App_Plugins`, or bare specifiers. **Pitfall:
  changed assets with an unchanged `version` keep serving from browser cache.** Code-split chunks get no query string, so build with content-hashed chunk names
  (Vite does by default). The `%CACHE_BUSTER%` token is deprecated, removal in Umbraco 20.
- In `Production` runtime mode manifests are cached for 30 days or until restart; otherwise 10 seconds.
- Each extension may carry `conditions`; all must be permitted.

### Custom manifest types

Source: https://docs.umbraco.com/umbraco-cms/17.latest/extend-your-project/backoffice-extensions/extending-overview/custom-extension-type.md

"The extension registry is an open system, which can hold any Extension Manifest Type... Types can be declared for re-useability/maintainability
or to open up for other package extensions." The pattern:

```ts
export interface MyExtensionManifestType extends ManifestBase { type: 'myPrefixedExtensionType'; }
declare global { interface UmbExtensionManifestMap { MyPrefixedExtensionManifest: MyExtensionManifestType } }
```

The docs say to prefix the type name. They say nothing about how the declaration reaches a consumer's compiler (see "Types delivery" below).

### Custom conditions

Source: https://docs.umbraco.com/umbraco-cms/17.latest/extend-your-project/backoffice-extensions/extending-overview/extension-types/condition.md

A condition is a class extending `UmbConditionBase` (implements `UmbExtensionCondition`), registered as `type: "condition"` with `api`, plus a
`declare global { interface UmbExtensionConditionConfigMap { ... } }` entry so the config validates in `UmbExtensionManifest`. "The Condition Config
Type alias should match the alias given when registering the condition." Built-ins used by collection/workspace code include
`Umb.Condition.WorkspaceAlias`, `Umb.Condition.WorkspaceEntityType`, `Umb.Condition.CollectionAlias`, `Umb.Condition.Entity.Type` (`match` or `oneOf`),
`Umb.Condition.SectionAlias`, `Umb.Condition.CurrentUser.GroupId`.

### Kinds

Source: https://docs.umbraco.com/umbraco-cms/17.latest/extend-your-project/backoffice-extensions/extending-overview/extension-types/kind.md

A kind is a `type: "kind"` manifest with `matchType`, `matchKind` and a `manifest` of defaults that extensions inherit via `kind: "..."`. The docs'
"Custom Kind Example" shows a package defining its own kind for a core type (`headerApp`) and extending the manifest interface for `meta`.
The docs give no naming rules for kind aliases beyond "unique". A package can also offer a kind for its own custom type.

### Global contexts and context tokens

Source: https://docs.umbraco.com/umbraco-cms/17.latest/extend-your-project/backoffice-extensions/extending-overview/extension-types/global-context.md

- Documented shape: a `*.context-token.ts` exporting `new UmbContextToken<MyContext>('MyContextAlias')`, a context class extending
  `UmbContextBase` that passes the token to `super`, and a `globalContext` manifest with `api`.
- "Use the context class rather than the interface as the generic type." The token's string "does not need to match the alias registered
  in the manifest."
- Explicitly "Prefer other context types such as Workspace Contexts over Global Contexts. Umbraco itself uses Global Contexts sparingly."
- Purpose listed includes "Share functionality with other extensions - allowing other package developers to access features and functionality within your package."
- Verified in the 17.8.0 `@umbraco-cms/backoffice` source (`libs/context-api/token/context-token.js`): a token is looked up by the string
  `contextAlias + '#' + apiAlias`. So a consumer that has its own copy of the token constant (or a types-only import plus a matching string) still
  resolves the provider; sharing module identity matters only if a `discriminator` function is used.

### Build and types for extension authors

Source: https://docs.umbraco.com/umbraco-cms/17.latest/extend-your-project/backoffice-extensions/development-flow/vite-package-setup.md

- Install `@umbraco-cms/backoffice@<your Umbraco version>` as a dev dependency (`--legacy-peer-deps` to avoid Monaco etc.), add
  `"types": ["@umbraco-cms/backoffice/extension-types"]` to `tsconfig.json`, and Vite `rollupOptions.external: [/^@umbraco/]` so the backoffice is
  not bundled.
- Vite library mode, ES format, `outDir` into `App_Plugins` or `wwwroot`.

### Entity types, workspaces, collections

Umbraco documents these as extension types (Trees, Workspaces, Collections, Entity Actions) but, in the pages read, gives no rule on naming or
publishing entity types. Not found in docs: any statement that entity-type strings are a public contract. Treat as unverified.

## What Umbraco itself publishes (observed in `@umbraco-cms/backoffice` 17.8.0, 17.8.2 is latest)

Source: npm tarball read locally; package page https://www.npmjs.com/package/@umbraco-cms/backoffice

- `package.json` `exports` has 141 subpath entries (`./context-api`, `./extension-api`, `./extension-types`, `./workspace`, `./collection`, `./entity-action`, ...). Runtime is in `dist-cms`; the
  `umbraco-package-schema.json` and a `custom-elements.json` ship in the package.
- Peer dependencies are Lit, UUI (`@umbraco-ui/uui ^2.1.1`), rxjs, and others. At runtime these are not installed by the extension; the host
  provides them via the server import map (the repo's `devops/importmap/index.js` generates `@umbraco-cms/backoffice/<subpath>` entries from
  the `exports` map).
- Per-feature constants follow `UMB_<FEATURE>_ENTITY_TYPE` / `UMB_<FEATURE>_ROOT_ENTITY_TYPE` string constants plus derived union types in an
  `entity.ts` (for example `UMB_DOCUMENT_ENTITY_TYPE = "document"`, `"document-root"`), tokens as `UMB_*_CONTEXT`.
- Versioning: https://umbraco.com/products/knowledge-center/versioning-and-release-cadence/ says minors (every 6 weeks) "will not contain
  breaking changes", removals are deprecated in a minor and removed in the following major, 17 is LTS, and add-on majors align with the CMS major. That page is about the
  public API generally; it does not name the backoffice TypeScript surface specifically. Whether npm subpath exports are covered by it is not
  stated; unverified. Breaking client changes do appear in major upgrade notes (for example the removed
  `@umbraco-cms/backoffice/external/tiptap` namespace in the breaking-changes overview,
  https://docs.umbraco.com/umbraco-cms/17.latest/get-started/upgrading-and-migrating/version-specific.md).

## What add-on and community packages publish

All figures read from the downloaded npm tarballs and, for Forms, its docs page.

| Package (npm) | Kind | Versions seen | Exposes |
| --- | --- | --- | --- |
| `@umbraco-forms/backoffice` | types only (`.d.ts`) | 17.7.1, 18.x | Manifest types (`formsFieldPreview`, `formsAnalyticsWidget`), entity-type constants (`forms-datasource`, `forms-prevalue`), context token `FORMS_CONTEXT`, base elements, a condition config; merges into `UmbExtensionManifestMap` and `UmbExtensionConditionConfigMap`. Peer: `@umbraco-cms/backoffice ^17.7.0`. `exports` has only `types`. |
| `@umbraco-commerce/backoffice` | types only, one rolled-up `.d.ts` | 17.2.13, 18.x | `UC_STORE_CONTEXT` token, kind manifests (`UC_*_DEFAULT_KIND_MANIFEST`), entity-type unions (`'uc:cart' \| 'uc:order'`), element base classes. Items marked `/** @public **/`. Peer `@umbraco-cms/backoffice ^17.0.0`. |
| `@umbraco-deploy/backoffice` | types only | 17.4.0 | Entities, repositories, a `cms-augmentations.d.ts`. Peer `^17.0.0`. |
| `@jumoo/usync` (uSync, community, MPL-2.0) | runtime JS + `.d.ts` | 17.6.x, 18.x | Subpath `exports` (`./api`, `./components`, `./constants`, `./tree`, `./dialogs`, `./signalr`); constants object, conditions, components. No peer deps declared. README: "Most packages do not need to export their client library as an npm package, unless your client library is also going to be used by child packages." |
| Umbraco UI Builder | not found on npm | | Configured mostly from C#; its docs (https://docs.umbraco.com/umbraco-ui-builder/17.latest/) show no client extension types package. Could not verify further. |
| Contentment (community) | not found on npm | | Repo exists (leekelleher/umbraco-contentment); I did not find a published extension-types package and did not read its source. Unverified. |

Sources: https://www.npmjs.com/package/@umbraco-forms/backoffice, https://www.npmjs.com/package/@umbraco-commerce/backoffice,
https://www.npmjs.com/package/@umbraco-deploy/backoffice, https://www.npmjs.com/package/@jumoo/usync,
Forms docs https://docs.umbraco.com/umbraco-forms/17.latest/developer/extending.md and
https://docs.umbraco.com/umbraco-forms/17.latest/developer/extending/backoffice-extension-points.md.

### Types delivery (how a consumer's compiler sees the package)

The Forms docs (the only first-party statement of this pattern) tell consumers to:
1. `npm install -D @umbraco-forms/backoffice@x.x.x`, matching the installed Forms version ("Ensure that you install the version ... compatible");
2. list the package in `tsconfig.json` `compilerOptions.types`, "so it includes the Global Types from the package. This enables you to utilize the
   declared Extension Types";
3. keep Umbraco's packages external in Vite.

Why step 2 matters: the `declare global` merges only load if the package is part of the program. Importing a value is not enough for
a type-only use.

### uSync's runtime-sharing pattern (observed, community)

Source: https://github.com/KevinJump/uSync (branch `v17/main`), `uSync.Backoffice.Management.Client/uSyncManifestReader.cs`,
`uSync.History/history-client/vite.config.ts`.

- The host package's manifest is built in C# (`IPackageManifestReader`) and supplies an `Importmap` mapping `@jumoo/usync` (and
  `@jumoo/uSync`, and the signalr subpath) to its single built file, with `?v={version}` appended.
- A child package (uSync.History) lists `/^@umbraco/` and `/^@jumoo\/usync/i` as Vite externals, so it imports the host's loaded code
  instead of bundling a second copy.
- Cost: the host must keep both casings and every exposed subpath in the importmap by hand, and the child breaks if the host is absent.
  Which of this is intentional design versus workaround I cannot tell from the source.

## Pitfalls (documented unless noted)

1. **Version bump is the cache key** (17.6+): unchanged `version` plus changed assets leaves stale JS in browsers.
2. **Do not bundle `@umbraco*`**; mark external. Do not install the backoffice's heavy peers (`--legacy-peer-deps`).
3. **Add the package to `tsconfig` `types`** or its global declarations are not seen (Forms docs).
4. **Match the npm package version to the product version**, as Forms and Commerce say; peer range tracks `@umbraco-cms/backoffice`.
5. **Importmap specifiers must be listed explicitly**: only the specifiers in `importmap.imports` resolve; there is no automatic exposure of
   a package's own subpaths (inference from the documented mechanism and the uSync listing; not stated as a rule).
6. **Prefer workspace contexts over global contexts** (Umbraco's own guidance).
7. **Manifest changes need a restart** in Production mode because manifests are cached (30 days otherwise).

## Recommendation for the Feature Flags 1.0 surface (input to #12)

This is advice, not decided. Current client state read from the repo: entity types `umbricians-feature-flags-root` and
`umbricians-feature-flags-flag`, workspace/collection/repository aliases in `Client/src/constants.ts`, a custom permission condition
(config type already merged into `UmbExtensionConditionConfigMap` under the key `umbriciansFeatureFlagsPermission`), and
a single bundle entry in `umbraco-package.json` with no `importmap`.

Promise (small, stable, string-based, nothing that needs runtime sharing):
- Entity type strings `umbricians-feature-flags-flag` and `-root`, workspace, collection and menu aliases. These are what other packages' conditions and
  manifests (`Umb.Condition.Entity.Type`, `WorkspaceAlias`, `CollectionAlias`) match on, so they are the real contract.
- The permission condition alias and its config type (already global-merged), documented as such.
- Element names `umbricians-feature-flags-*`: documented as not part of the surface unless a consumer is told to rely on them. Elements are
  replaceable through manifests, so aliases matter more than tag names.
- Extension points for others: attach workspace views, workspace actions and entity actions to the flag entity type through conditions on
  the entity type and workspace alias. This needs no new manifest types.

Defer until there is a consumer:
- A types-only npm package (`@umbricians/feature-flags-backoffice` or similar) following the Forms and Commerce pattern, with major version aligned to the
  package and peer `@umbraco-cms/backoffice` set to the same major. It gives consumers the constants, the condition config, and the manifest
  types. Forms' docs make `tsconfig types` an explicit consumer step; copy that text.
- A context token for the summary/workspace context (`summary.context.ts` exists). Tokens resolve by string, so publishing only the token constant and
  type is cheap. Publishing a global context is not needed.
- Custom manifest types or kinds: Umbraco documents the mechanism, but nothing in the current design needs a new type.

Avoid for 1.0:
- Runtime npm code plus `importmap` (the uSync pattern). It forces the importmap, casing and subpath upkeep and a hard runtime dependency
  between packages. uSync's own README says most packages do not need it.
- Promising internals (repositories, `api/` client types, collection view elements).

Already covered: `.github/workflows/build.yml` stamps the release version into `umbraco-package.json` (`build/stamp-version.mjs`), which is
what the 17.6+ cache-busting value is derived from. The `0.0.0` in `Client/public/` is the dev placeholder.

## Not verified

- Whether Umbraco's no-breaking-changes-in-minors promise covers the `@umbraco-cms/backoffice` TypeScript API and the entity-type strings.
- Contentment and UI Builder client extension surfaces (no types package found; source not read).
- Whether Umbraco recommends any specific npm package naming for third-party types packages (found none).
- Umbraco 18/19 behaviour (npm shows 18.x and 19.0 RC for add-ons; not researched).
