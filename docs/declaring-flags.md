# Declaring and checking flags

For developers. Feature Flags doesn't have its own flag format or its own way of checking flags: flags are
[Microsoft.FeatureManagement](https://learn.microsoft.com/azure/azure-app-configuration/feature-management-dotnet-reference)
flags, declared and checked as Microsoft documents. What the package adds is a backoffice where editors change them
per site, and a few details that help editors find them.

## Why only developers declare flags

A flag does nothing until code checks it, so the flag and the code that checks it are deployed together. Editors
switch flags on and off, but they can't create or delete them. Deploying a new flag never needs a backoffice step,
and every environment lists the same flags.

## In appsettings

Use Microsoft's `feature_management` schema:

```json
{
  "feature_management": {
    "feature_flags": [
      {
        "id": "Checkout.ExpressPay",
        "enabled": false,
        "description": "One-click payment for returning members.",
        "section": "Checkout",
        "tags": ["payments", "beta"],
        "public": true
      }
    ]
  }
}
```

Microsoft's older `FeatureManagement` schema works too:

```json
{
  "FeatureManagement": {
    "Search.Suggestions": false,
    "Checkout.GiftCards": { "EnabledFor": [{ "Name": "AlwaysOn" }], "Description": "Pay with a gift card." }
  }
}
```

Anything Microsoft supports can go in a declaration: targeting, time windows, percentages and variants. The
backoffice edits flags that are off, on for everyone, or on for an audience of members and groups. Flags that use
anything else show as **Custom** and are read-only there, and nothing an editor saves drops those parts.

Because these are ordinary configuration values, they can come from any configuration source: per-environment
appsettings files, environment variables, or Azure App Configuration's feature flags loaded into configuration.

## In code

For sites that would rather not use configuration, declare flags in a composer, or in `Program.cs` before Umbraco is
built:

```csharp
using Umbricians.FeatureFlags.Extensions;

public class SiteFlagsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.AddFeatureFlags(flags => flags
            .Declare("DarkMode", description: "Dark colours across the site.", tags: ["ui"])
            .Declare("Pricing.MemberDiscount", description: "10% off for the members it is on for.", section: "Pricing"));
}
```

`Declare(name, enabled, description, section, tags, isPublic)` declares a flag that is off, or on for everyone when `enabled` is
`true`, until an editor changes it. Leave new flags off.

`Declare(FeatureDefinition, description, section, tags, isPublic)` takes any Microsoft definition, for flags that need a filter
or variants. Most of those are easier to write in appsettings.

## Declared in both places

When code and configuration both declare a flag, configuration's declaration is used. The package logs a warning at
startup naming the flag, and the backoffice marks it **Declared twice**. It is usually a mistake, so remove one.

## Details for editors

Each flag can have:

| | appsettings field | Code parameter | Shown |
| --- | --- | --- | --- |
| Description | `description` (`Description` in the older schema) | `description` | Under the flag's name, in the list and the editor. |
| Section | `section` (`Section`) | `section` | The section of the list the flag is in. When there is none, the text before the first `.` in the flag's name; a name without a `.` is in No section. |
| Tags | `tags` (`Tags`) | `tags` | Under the description; the list can be filtered by tag. |
| Public | `public` (`Public`) | `isPublic` | Whether the flag may be shown to visitors' browsers. Flags are private unless declared public. |

`MakePublic("Checkout.ExpressPay", "DarkMode")` on the builder marks flags public whichever flag source declares them,
such as a site's own `IFeatureDefinitionProvider`. Names are compared without regard to case. A flag is public when any
of these says so, so `MakePublic` wins over a flag that says `"public": false` in configuration. Only public flags are served by the [headless endpoint](headless.md).

Microsoft ignores these fields, so they are harmless. Naming flags `Area.Feature`, such as `Checkout.ExpressPay`,
puts them in sections with nothing more to write. On a site with many flags, the list's search, filters and paging keep them
manageable.

Flags from a flag source of the site's own (a custom `IFeatureDefinitionProvider`) have no details, and are put in sections by
name.

## Checking a flag

Exactly as on any site using Microsoft.FeatureManagement. Code that checks flags doesn't know Feature Flags is there.

**In Razor**, with Microsoft's tag helper. Add it in `_ViewImports.cshtml`:

```cshtml
@addTagHelper *, Microsoft.FeatureManagement.AspNetCore
```

```cshtml
<feature name="Search.BetaSearch">
    <input placeholder="Ask anything">
</feature>
<feature name="Search.BetaSearch" negate="true">
    <input placeholder="Search products">
</feature>
```

**In a controller or service**, with `IFeatureManager`, or `IFeatureManagerSnapshot` for an answer that stays the
same for the rest of the request:

```csharp
public class ProductController(IFeatureManagerSnapshot features) : Controller
{
    public async Task<IActionResult> Index()
    {
        decimal price = 149.00m;
        if (await features.IsEnabledAsync("Pricing.MemberDiscount"))
        {
            price *= 0.9m;
        }

        return View(new ProductModel(price));
    }
}
```

A flag checked in C# can change anything the server works out, not only what a page shows: a price, a delivery
estimate, which payment provider is called.

**On a whole action or controller**, with `[FeatureGate("Checkout.NewFlow")]`, which returns 404 while the flag is off
for the visitor.

Use constants for flag names, so a typo is a build error rather than a flag that is always off. An undeclared name
is simply off.

## How a flag is decided

1. If the flag is declared, an editor has saved a setting for it, `Overrides` isn't `ReadOnly` and the setting can be
   read, it is used.
2. Otherwise the flag's declaration in the flag source (configuration or the site's own provider) is used.
3. Otherwise its declaration in code.
4. A flag nobody declares is off.

Editors' settings are kept in memory, so checking a flag never reads the database. When an editor saves, every server
reloads them within seconds.

## Changing flags from code

`IFeatureFlagService` reads flags and changes their overrides, the way the backoffice does but without permission
checks, since code isn't a backoffice user:

```csharp
public class Seeder(IFeatureFlagService flags)
{
    public async Task SeedAsync()
    {
        foreach (string name in new[] { "Checkout.NewFlow", "Search.BetaSearch" })
        {
            FeatureFlagResult result = await flags.SwitchAsync(name, on: true);
            if (!result.IsSaved)
            {
                // NotFound, NotAllowed (see result.Reason) or Cancelled (see result.Messages)
            }
        }
    }
}
```

The rules are the backoffice's: nothing can be changed when `Overrides` is `ReadOnly`, a flag nobody declares can't be
saved, and a flag with conditions the backoffice can't edit can't be switched. These come back as a result, not an
exception. When the call returns, the next check on this server sees the change; other servers see it within
seconds. To change several flags, call it for each.

Change overrides through the service, not `IFeatureFlagOverrideStore`: writing to the store skips the rules and the
refresh on other servers. The store is for keeping overrides somewhere other than the Umbraco database.

## Testing with flags

There is no test package; Microsoft's own types are enough.

**Unit tests** mock Microsoft's `IFeatureManager` (or `IVariantFeatureManager`), so the code under test never meets
Feature Flags:

```csharp
var features = new Mock<IFeatureManager>();
features.Setup(f => f.IsEnabledAsync("Checkout.NewFlow")).ReturnsAsync(true);
var controller = new CheckoutController(features.Object);
```

**Integration tests** have two ways to put flags in a known state:

- Set `Umbricians:FeatureFlags:Overrides` to `ReadOnly`, so editors' saved settings are ignored, and declare the flags
  in an in-memory `feature_management` section. This is the simplest, and what to prefer.
- Or change flags through `IFeatureFlagService`, which goes through the real path, overrides and all.

**As a particular member**, sign in as a test member, or register a fixed `ITargetingContextAccessor`, which replaces
the package's:

```csharp
public class TestingAccessor(string userId) : ITargetingContextAccessor
{
    public ValueTask<TargetingContext> GetContextAsync() =>
        ValueTask.FromResult(new TargetingContext { UserId = userId, Groups = [] });
}
```

A whole example with `WebApplicationFactory`:

```csharp
public class CheckoutTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CheckoutTests(WebApplicationFactory<Program> factory) =>
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Umbricians:FeatureFlags:Overrides"] = "ReadOnly",
                ["feature_management:feature_flags:0:id"] = "Checkout.NewFlow",
                ["feature_management:feature_flags:0:enabled"] = "true",
            }));
            builder.ConfigureServices(services => services.AddSingleton<ITargetingContextAccessor>(new TestingAccessor("member-1")));
        }).CreateClient();

    [Test]
    public async Task The_new_checkout_is_shown_when_the_flag_is_on()
    {
        string html = await _client.GetStringAsync("/checkout");
        Assert.That(html, Does.Contain("New checkout"));
    }
}
```

## When a flag is no longer needed

Remove the code that checks it, then the declaration. If an editor had changed it, its setting stays in the database
as an **undeclared flag**: it has no effect, is listed separately in the backoffice, and an editor can remove it.
