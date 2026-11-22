# Glossary

The words this codebase uses for its domain. Code, docs and reviews should use them as defined here.

## Audience

Who a flag is on for when its state is *on for an audience*: chosen members, chosen groups, a share of
everyone or of a group (signed-in members only), and members or groups left out. It is Microsoft's targeting `Audience`, stored and evaluated as Microsoft
defines it. Not a separate list of people that Feature Flags keeps.

## Cause

What changed an override: an editor in the backoffice, a developer's code, or member clean-up (taking a deleted
member or group out of every audience).

## Condition

A rule a flag must pass to be on for a request, such as an audience, a time window or a developer's own rule.
Microsoft calls it a feature filter. A condition the package or a developer has registered can be edited in the
backoffice; any other makes the flag's state custom. _Avoid_: filter (except when naming Microsoft's).

## Declared flag

A flag a developer has declared, in appsettings or in code, through any source of feature definitions the site uses.
Only declared flags exist: editors can't create or delete flags in the backoffice. Not every declared flag has an
override.

## Default

What a declared flag does before any editor changes it: its definition as the flag source gives it. New flags
should be declared off.

## Developer

The person who installs Feature Flags on a site and declares, checks and extends flags in code. Not an editor,
though one person may be both. _Avoid_: implementer, integrator.

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
Not a backoffice user group, and not a [section](#section) of the flag list.

## Group provider

Code a site supplies to put members in groups by a rule, such as "joined in the last 30 days" or "premium in the
CRM". It names the groups it offers, so editors can pick them, and says which of them a member is in.

## Member

A person signed in to the site's front end through Umbraco members. Who flags are evaluated for. A visitor who
isn't signed in is anonymous: in no audience, and never counted by a percentage.

## Override

An editor's setting for a flag, stored in the site's database, which replaces the flag's default on that site.
Each site has its own overrides. Removed by resetting.

## Public flag

A flag a developer has said a front end may ask about. Flags are private unless declared public, because their
names can reveal features not yet released. The [headless endpoint](docs/headless.md) serves only public flags. Editors can see whether a flag is public but not change it.

## Reset

Removing a flag's override, so the flag goes back to its default.

## Root

A top-level node in the Content tree and the domains assigned to it, such as the UK shop, on an install that runs
several. A flag can be limited to chosen roots, which are stored by the root's key. Not the [site](#site): one site can have many roots. _Avoid_: site
(for a root), website, except in the backoffice's labels for editors, such as "Only on these sites".

## Section

The part of the flag list a flag is shown in, such as "Checkout", to help editors find it. Set with `section` where
the flag is declared, or taken from the text before the first `.` in its name. It has no effect on who gets the
feature. Not a [group](#group).

## Site

One Umbraco install in one environment, with its own database and so its own overrides. It may have several
[roots](#root).

## State

How a flag is evaluated: *off*, *on for everyone*, *on for an audience*, or *on with conditions*. In Microsoft's
terms: disabled, enabled with no conditions, enabled with only the targeting filter, and enabled with other
registered conditions. A flag whose definition uses a condition nobody has registered, or variants, is *custom*: the
backoffice shows it but can't change it.

## Undeclared flag

An override whose flag no flag source declares any more, usually because a developer removed it. It has no effect
and is kept until an editor removes it, so rolling back a deploy loses nothing.
