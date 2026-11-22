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

Editors can switch a flag with conditions on and off, and the list shows each condition's summary. In the flag's
editor they can add, change and remove conditions that have a backoffice editor, and choose Any or All. A *custom* flag
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
manifest of the type `umbriciansFeatureFlagsCondition`, whose `meta.name` is the C# condition's `Name`. The flag
editor's **Add condition** menu lists every registered condition that has one, and shows its element for each condition
the flag has. Names match the way the C# half's do (ignoring case, short names allowed). The element's contract is:

- **in:** the property `value`, the condition's parameters as a JSON object (an empty object for a new condition), and
  `readonly` when the editor may not change the flag;
- **out:** when the editor changes the parameters, set `value` to the new JSON object and dispatch a `change` event
  (Umbraco's `UmbChangeEvent`, which bubbles). The flag editor then reads `element.value`. Fire it for every change
  the editor makes, and not for events from the inputs inside your element: stop those at your own root.

That is everything the contract promises. The flag editor checks nothing about the parameters in the browser; the C#
`Validate` does it on save, and its problems are shown to the editor.

With only one half registered:

- **C# only:** the flag shows the condition's summary and "This condition has no editor", and can't be saved from the
  backoffice, except by choosing **On for everyone**. Its parameters are changed in appsettings or with
  `IFeatureFlagService`.
- **Backoffice only:** the manifest is ignored, since the server doesn't know the condition.

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
        CultureInfo current = CultureInfo.CurrentUICulture;
        // "fr" matches every French culture; "fr-CA" only that one.
        return Task.FromResult(settings.Cultures.Any(culture =>
            string.Equals(culture, current.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(culture, current.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)));
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

Register both, in a composer:

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.FeatureManagement;
using Umbraco.Cms.Core.Composing;
using Umbricians.FeatureFlags.Extensions;

public class CultureComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Feature Flags has already called AddFeatureManagement(); this adds the filter as AddFeatureFilter<T>() does.
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFeatureFilterMetadata, CultureFilter>());
        builder.AddFeatureFlags(flags => flags.AddCondition<CultureCondition>());
    }
}
```

Don't call `AddFeatureManagement().AddFeatureFilter<CultureFilter>()` here: after Umbraco is added, a second
`AddFeatureManagement()` registers Microsoft's filters twice (see
[Getting started](getting-started.md#feature-management-registered-twice)). It is fine in `Program.cs` before
`CreateUmbracoBuilder()`, where Feature Flags then uses it.

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

The editor for it is a Lit element and a manifest. The element is the whole of the backoffice half:

```ts
// culture-condition.element.ts
import { css, customElement, html, property } from "@umbraco-cms/backoffice/external/lit";
import { UmbLitElement } from "@umbraco-cms/backoffice/lit-element";
import { UmbChangeEvent } from "@umbraco-cms/backoffice/event";

@customElement("contoso-culture-condition")
export class CultureConditionElement extends UmbLitElement {
  /** The condition's parameters: { "Cultures": [ "fr", "de" ] }. */
  @property({ attribute: false })
  value: { Cultures?: Array<string> } = {};

  @property({ type: Boolean })
  readonly = false;

  #change(event: Event) {
    event.stopPropagation(); // the input's own change isn't the element's
    const text = (event.target as HTMLInputElement).value;
    this.value = { Cultures: text.split(",").map((culture) => culture.trim()).filter(Boolean) };
    this.dispatchEvent(new UmbChangeEvent());
  }

  override render() {
    return html`<uui-input
      label="Cultures"
      placeholder="fr, de"
      .value=${(this.value.Cultures ?? []).join(", ")}
      ?readonly=${this.readonly}
      @change=${this.#change}></uui-input>`;
  }
}

export { CultureConditionElement as element };
```

```ts
// umbraco-package.json's bundle, or any bundle's manifests
export const manifests: Array<UmbExtensionManifest> = [
  {
    type: "umbriciansFeatureFlagsCondition",
    alias: "Contoso.Condition.Culture",
    name: "Culture Condition Editor",
    element: () => import("./culture-condition.element.js"),
    meta: { name: "Contoso.Culture" },
  },
];
```

Register it like any Umbraco extension, with a `bundle` in your package's `umbraco-package.json`. The culture
condition now shows in **Add condition**, and the flag editor saves `{ "Cultures": [ "fr", "de" ] }`.

## The package's own conditions

The package registers its own conditions through the same contract as yours, so they appear in **Add condition** beside
any you add.

### Audience (`Microsoft.Targeting`)

Members, member groups and groups from group providers, with a percentage of everyone else and of each group, and the
members and groups left out. See [Audiences](audiences.md).

### Time window (`Microsoft.TimeWindow`)

"On from a time until a time", with either end optional; at least one is needed, and the start must be before the end.

- Editors enter and see times in their browser's time zone, with the UTC equivalent beside each. The backoffice stores
  them as UTC, in ISO 8601 ending in `Z`, such as `2026-12-01T09:00:00Z`.
- Microsoft reads `Start` and `End` as a `DateTimeOffset`, so values written some other way still work. ISO 8601 and
  RFC 1123 (`Tue, 01 Dec 2026 09:00:00 GMT`) are both read and shown, and are only rewritten as ISO 8601 when an editor
  changes that end. A time with no zone is read by Microsoft in the server's own time zone; the backoffice can't
  know which, so it shows that end as unreadable until an editor chooses a new time.
- A window with a **recurrence** shows its summary (ending "(repeats)") but can't be changed: the flag shows as **Custom**,
  with "Repeating windows can't be edited yet" as the reason. Change it where the flag is declared.

### Root: "Only on these sites" (`Umbricians.Root`)

For a site with several [roots](../GLOSSARY.md#root) (top-level content nodes, each usually a site with its own domain).
The flag is on only for requests in the roots an editor picks. Parameters: `{ "Roots": [ "<root key>", ... ] }`; roots are
stored by key, so renaming a root or changing its domains changes nothing.

**Which root a request is in**, first match wins:

1. **The content the request found.** The top-level ancestor of the page Umbraco routed the request to.
2. **The `Start-Item` header**, for headless requests, read as the Delivery API reads it: a root's key (a key of a page
   that isn't a root names nothing), or a root's URL segment, ignoring case and leading or trailing slashes. Only
   published roots count. The package reads this itself, so it works whether or not the Delivery API is installed.
3. **The domain** Umbraco matches to the request's host and path (the same match it uses to route pages). A domain
   assigned to a page below the root puts the request in that page's root.
4. **Otherwise no root**, and the condition fails. The same when there is no request, such as in a background job.

The answer is worked out once for each request.

**A deleted root** stays in the condition by its key. The summary says "a deleted site" and it never matches (a root in
the recycle bin, or moved below another page, counts as deleted). The health check warns; nothing is
cleaned up automatically, so an editor removes it from the condition.

Adding Root to a flag that has another condition switches the flag to **All**, since "this audience or this site" is
rarely what editors mean; they can change it back.
