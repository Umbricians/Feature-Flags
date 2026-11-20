# Glossary

The words this codebase uses for its domain. Code, docs and reviews should use them as defined here.

## Audience

Who a flag is on for when its state is *on for an audience*: chosen members, chosen groups, and later a
percentage of everyone and exclusions. It is Microsoft's targeting `Audience`, stored and evaluated as Microsoft
defines it. Not a separate list of people that Feature Flags keeps.

## Declared flag

A flag a developer has declared, in appsettings or in code, through any source of feature definitions the site uses.
Only declared flags exist: editors can't create or delete flags in the backoffice. Not every declared flag has an
override.

## Default

What a declared flag does before any editor changes it: its definition as the flag source gives it. New flags
should be declared off.

## Editor

A backoffice user who can see or change flags. Not a member.

## Feature

Something a site's code shows, hides or changes depending on a flag. The package never knows what a feature does,
only its flag.

## Flag

Short for feature flag: a named switch, such as `NewCheckout`, that site code checks with Microsoft's
`IFeatureManager`, `[FeatureGate]` or `<feature>`. Its name is the key code checks; names are compared as Microsoft
compares them.

## Flag source

Where declared flags and their defaults come from: appsettings, environment variables, Azure App Configuration, code,
or a site's own `IFeatureDefinitionProvider`. Feature Flags wraps the site's flag source; it doesn't replace it.

## Group

A name a member can belong to for targeting: one of their member groups, or a name a group provider gives them.
Not a backoffice user group.

## Group provider

Code a site supplies to put members in groups by a rule, such as "joined in the last 30 days" or "premium in the
CRM". It names the groups it offers, so editors can pick them, and says which of them a member is in.

## Member

A person signed in to the site's front end through Umbraco members. Who flags are evaluated for. A visitor who
isn't signed in is anonymous and is in no audience.

## Override

An editor's setting for a flag, stored in the site's database, which replaces the flag's default on that site.
Each site and environment has its own overrides. Removed by resetting.

## Reset

Removing a flag's override, so the flag goes back to its default.

## State

How a flag is evaluated: *off*, *on for everyone*, or *on for an audience*. In Microsoft's terms: disabled,
enabled with no conditions, and enabled with the targeting filter.

## Undeclared flag

An override whose flag no flag source declares any more, usually because a developer removed it. It has no effect
and is kept until an editor removes it, so rolling back a deploy loses nothing.
