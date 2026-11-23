# @umbricians/feature-flags

TypeScript types for extending the [Umbricians Feature Flags](https://github.com/Umbricians/Feature-Flags) backoffice
(an Umbraco package): the entity types and aliases it promises, the manifest type and element contract of a
condition's editor, the read-only flag workspace context, and the shapes of the Management API.

Optional, and types only: the NuGet package `Umbricians.FeatureFlags` is all a site needs, and this package only
helps TypeScript check your extension. There is no runtime code. There are no constants to import: write the strings yourself, and type them with the string
literal aliases (such as `FeatureFlagEntityType`) where you want a typo to fail to compile. See
[Extending the backoffice](https://github.com/Umbricians/Feature-Flags/blob/main/docs/backoffice.md#extending-the-backoffice).

The surface is promised within one Umbraco major version. Each major has its own version of this package, with its peer
dependency on `@umbraco-cms/backoffice`: `^17.8.2` for 17.x, `^18.3.2` for 18.x and `^19.0.0-rc1` for 19.x. Install the
one that matches your Umbraco major, for example `npm install -D @umbricians/feature-flags@18`.
