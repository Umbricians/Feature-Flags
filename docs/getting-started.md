# Getting started

This guide takes you from installing Feature Flags to a flag that is on for a few members on one page of your site,
switched from the backoffice. It takes about ten minutes.

## Requirements

- Umbraco 17.8.0 or later.
- A developer to declare flags and check them in the site's code. Editors switch flags, but they can't create them.

## 1. Install the package

```bash
dotnet add package Umbricians.FeatureFlags
```

Restart the site. On first start the package:

- adds Microsoft.FeatureManagement, unless the site already uses it;
- adds one table to the database, `umbriciansFeatureFlagsOverride`, for editors' settings;
- gives the Administrators user group its two permissions.

Nothing else needs configuring.

## 2. Declare a flag

Add a flag to `appsettings.json` in Microsoft's `feature_management` section. Start it off:

```json
{
  "feature_management": {
    "feature_flags": [
      {
        "id": "Checkout.ExpressPay",
        "enabled": false,
        "description": "One-click payment for returning members."
      }
    ]
  }
}
```

The text before the first `.` is the flag's section in the backoffice, so this flag is listed under "Checkout". You can
declare flags in code instead; see [Declaring and checking flags](declaring-flags.md).

## 3. Check it in the site

Add Microsoft's tag helpers to `Views/_ViewImports.cshtml`:

```cshtml
@addTagHelper *, Microsoft.FeatureManagement.AspNetCore
```

Then wrap the part of the page the flag switches:

```cshtml
<feature name="Checkout.ExpressPay">
    <a class="button" href="/express-pay">Buy now with ExpressPay</a>
</feature>
```

Reload a page that uses it: the button isn't there, because the flag is off.

## 4. Switch it on in the backoffice

1. Go to **Settings**, and choose **Feature Flags** under Advanced in the tree.
2. Choose **ExpressPay** under Checkout.
3. Choose **On for an audience**, then **Choose** under Members and pick one or two members.
4. Choose **Save**.

<img src="images/feature-flags-editor.png" alt="The editor for Checkout.ExpressPay: State is On for an audience, and the audience lists three members, with Reset to default and Save at the bottom">

Sign in on the front end as one of those members and the button appears; sign in as anyone else, or sign out, and it
doesn't. On a load-balanced site every server picks the change up within seconds.

To hand the flag back to the developers' setting, open it and choose **Reset to default**. The list's switch turns a
flag off at once, and asks first before turning one on. [Using the backoffice](backoffice.md) covers the rest of the
screens.

## 5. Give editors access

Administrators can already change flags. For anyone else, go to **Users → User groups**, open their group, and under
**Permissions → Feature Flags** tick **View feature flags**, and **Change feature flags** if they should change them.

## Before you go live

- [ ] Every flag that switches on unfinished work is declared `"enabled": false`, so a deploy never turns it on by
      itself.
- [ ] Audiences in appsettings name member groups, not members. A member's key is different in each environment's
      database, so choose individual members in each environment's backoffice.
- [ ] Each environment has its own settings: switching a flag on in staging doesn't switch it on in production.
- [ ] Only the user groups that should change flags on production have **Change feature flags**.
- [ ] If your flag source, such as Azure App Configuration, should stay in charge, set
      `Umbricians:FeatureFlags:Overrides` to `ReadOnly` (see the [README](../README.md#configuration)).
- [ ] The startup log has no warnings from Feature Flags (see below).
- [ ] Pages whose content changes per member aren't served from a shared cache, such as output caching or a CDN,
      or one member's version is shown to everyone.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| A flag isn't in the backoffice list. | Nothing declares it, or the site was not restarted after it was added. | Declare it in appsettings or code and restart. Flags can't be added in the backoffice. |
| A saved change makes no difference on the site. | The page is cached, the site checks a different flag name, or `Overrides` is `ReadOnly`. | Check the name matches exactly, and that the page isn't served from a cache. A read-only site says so at the top of the list. |
| A flag is on for an audience, but a member in it doesn't get the feature. | They aren't signed in as a member, or they're in the audience by a group the site's group provider doesn't put them in. | Sign in on the front end, not only in the backoffice: backoffice users aren't members. |
| A flag shows **Custom** and can't be edited. | Its definition uses something the backoffice has no screen for yet, such as a time window, a percentage or variants. | Change it where it is declared. Saving never drops these parts. |
| A flag shows **Declared twice**. | Code and configuration both declare it. Configuration's declaration is used. | Remove one of the two. |
| The list has **Undeclared flags**. | An editor changed a flag that the developers have since removed. Its setting has no effect. | Open the list's Show filter, choose Undeclared flags, and remove them. |
| The startup log says Microsoft.FeatureManagement is registered more than once. | The site calls `AddFeatureManagement()` after Umbraco is added. Checking a flag with a time window then throws. | Remove the call, or move it before Umbraco is added so Feature Flags uses it. |
| The startup log says the site registered its own flag source after Feature Flags. | A custom `IFeatureDefinitionProvider` was registered after Umbraco, so editors' settings don't apply. | Register it before Umbraco is added. |
| A member group named in appsettings shows as "1 other group" in the editor. | Audiences in appsettings can name a member group by name; the backoffice picker works with groups by key. The flag still works on the site. | Choose the group again in the backoffice if editors should manage it there. |
