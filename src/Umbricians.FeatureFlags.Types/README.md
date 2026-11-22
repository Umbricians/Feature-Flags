# @umbricians/feature-flags

TypeScript types for extending the [Umbricians Feature Flags](https://github.com/Umbricians/Feature-Flags) backoffice
(an Umbraco package): the entity types and aliases it promises, the manifest type and element contract of a
condition's editor, the read-only flag workspace context, and the shapes of the Management API.

Types only: there is no runtime code. A declared constant has no value at runtime, so write the string itself; the
literal types make a typo fail to compile. See
[Extending the backoffice](https://github.com/Umbricians/Feature-Flags/blob/develop/docs/backoffice.md#extending-the-backoffice).

The surface is promised within one Umbraco major version; this package's peer dependency is `@umbraco-cms/backoffice` `^17.0.0`.
