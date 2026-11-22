# Conditions

A **condition** is a rule a flag must pass to be on for a request: an audience, a time window, or a rule of your own,
such as "the request is in French". Microsoft calls it a feature filter. A flag with no conditions is on for everyone
when it is switched on; with conditions, it is on when they hold. A flag with several conditions needs any one of
them, or all of them, by its `requirement_type` (`Any` is Microsoft's default).

## What the backoffice does with them

A flag's *state* says how the backoffice sees it:

| State | When |
| --- | --- |
| Off | switched off; its conditions are kept for when it is switched on again |
| On for everyone | on, with no conditions |
| On for an audience | on, with only an audience (the `Microsoft.Targeting` filter) |
| On with conditions | on, with conditions that are all **registered** |
| Custom | on, with a filter nobody registered a condition for, or with variants |

Editors can switch a flag with conditions on and off, and the list shows each condition's summary. A *custom* flag
is shown but never changed: its reason names the filter nobody registered. Registering a condition is what tells the
package that you vouch for its parameters.

Every save, from the backoffice or from `IFeatureFlagService`, is checked by each condition's own `Validate`. If there
are problems the save is refused (`NotAllowed`, and a 409 from the Management API) and the problems are in
`FeatureFlagResult.Problems`, and in the `problems` list of the API's problem details.

## Writing your own

A condition has two halves.

1. **Microsoft's feature filter**, which evaluates it. Write and register it the usual Microsoft way
   (`AddFeatureFilter<T>()`); the package doesn't change that.
2. **`IFeatureFlagCondition`**, which tells the backoffice about it: the C# half described here.

```csharp
public interface IFeatureFlagCondition
{
    string Name { get; }                                   // the Microsoft filter it edits
    string DisplayName { get; }
    IEnumerable<string> Validate(JsonObject parameters);   // problems to show the editor; none means valid
    string Describe(JsonObject parameters);                // a one-line summary for the list
}
```

Register it on the Feature Flags builder, once for each condition:

```csharp
builder.AddFeatureFlags(flags => flags.AddCondition<CultureCondition>());
```

It is registered as a singleton, so keep it free of per-request state. Registering it directly, with
`services.AddSingleton<IFeatureFlagCondition, CultureCondition>()`, works too.

**Names match the way Microsoft's do.** `Name` is the filter's name as Microsoft knows it, such as `Contoso.Culture`.
Flags may write it with or without its namespace, and without regard to case: `Contoso.Culture`, `Culture` and
`culture` all find it. (Microsoft doesn't strip a trailing "Filter" from the name a flag uses, so `CultureFilter` in a
flag matches nothing, here or there.)

**The backoffice half.** To edit a condition's parameters in the backoffice, the condition also needs an editor: a
manifest of the type `umbriciansFeatureFlagsCondition`. That arrives with the condition editor, and this page will
describe it then. Until it does, a registered condition is validated, summarised in the list and kept when an editor
switches the flag, but its parameters are changed in appsettings or with `IFeatureFlagService`.

### Example: a culture condition

On for requests in chosen cultures, such as French only.

The Microsoft filter reads the request's culture and compares it with the parameters:

```csharp
using System.Globalization;
using Microsoft.FeatureManagement;

[FilterAlias("Contoso.Culture")]
public sealed class CultureFilter : IFeatureFilter
{
    public Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context)
    {
        var settings = context.Parameters.Get<CultureFilterSettings>() ?? new();
        string current = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Task.FromResult(settings.Cultures.Contains(current, StringComparer.OrdinalIgnoreCase));
    }
}

public sealed class CultureFilterSettings
{
    public string[] Cultures { get; set; } = [];
}
```

Its condition says what a good set of parameters is and how to put them in a line:

```csharp
using System.Text.Json.Nodes;
using Umbricians.FeatureFlags.Flags;

public sealed class CultureCondition : IFeatureFlagCondition
{
    public string Name => "Contoso.Culture";

    public string DisplayName => "Culture";

    public IEnumerable<string> Validate(JsonObject parameters)
    {
        if (parameters["Cultures"] is not JsonArray { Count: > 0 })
        {
            yield return "Choose at least one culture.";
        }
    }

    public string Describe(JsonObject parameters) =>
        string.Join(" and ", (parameters["Cultures"] as JsonArray ?? []).Select(culture => culture?.ToString()));
}
```

Register both:

```csharp
builder.Services.AddFeatureManagement().AddFeatureFilter<CultureFilter>();
builder.AddFeatureFlags(flags => flags.AddCondition<CultureCondition>());
```

A flag can now use it from appsettings:

```json
{ "id": "Home.French", "enabled": true,
  "conditions": { "client_filters": [ { "name": "Contoso.Culture", "parameters": { "Cultures": [ "fr", "de" ] } } ] } }
```

The list shows it as *On with conditions*, summarised "fr and de". Saving it with no cultures, from code, is refused:

```csharp
FeatureFlagResult result = await flags.SaveAsync("Home.French",
    new FlagSettings(true, [new FlagCondition("Contoso.Culture", new JsonObject { ["Cultures"] = new JsonArray() })]));
// result.Status is NotAllowed; result.Problems is ["Culture: Choose at least one culture."]
```

## The audience

The package's own audience is a condition registered the same way, as `Microsoft.Targeting` ("Audience"). Percentages
and exclusions are Microsoft's, but the backoffice can't edit them yet, so saving them is refused for now.
