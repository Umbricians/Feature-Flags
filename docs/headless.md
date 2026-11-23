# Headless front ends

A front end that Umbraco doesn't render, such as a Next.js or Astro site fed by the Delivery API, can't use
`IFeatureManager`. Feature Flags gives it an endpoint that says which flags are on for the request.

It is **off until you switch it on**, and it only ever tells a front end about flags you have marked public.

## Switching it on

```json
{
  "Umbricians": {
    "FeatureFlags": {
      "Headless": { "Enabled": true }
    }
  }
}
```

It sits beside `Overrides`, so you can switch it on for one environment only. While it is off, the endpoint answers
`404`. The health check warns when it is on but no flag is public.

## Marking a flag public

Flags are private by default. A flag is public when any of these says so:

```csharp
// In code
builder.AddFeatureFlags(flags => flags.Declare("Checkout.ExpressPay", isPublic: true));

// For a flag from any flag source, such as your own provider or Azure App Configuration
builder.AddFeatureFlags(flags => flags.MakePublic("Checkout.ExpressPay"));
```

```json
{
  "feature_management": {
    "feature_flags": [ { "id": "Checkout.ExpressPay", "enabled": true, "public": true } ]
  }
}
```

The flag list shows which flags are public. Editors can't change it: it is a developer's decision.

## Calling it

```
GET /umbraco/umbriciansfeatureflags/delivery/v1/flags
```

It answers with every public flag and whether it is on for this request:

```json
{ "Checkout.ExpressPay": true, "Search.Beta": false }
```

It doesn't return audiences, conditions or variants. Audiences, time windows and Root conditions all apply, because
each flag is checked the way your own code would check it.

### Access

The endpoint follows the Delivery API's access settings, `Umbraco:CMS:DeliveryApi`:

- With `PublicAccess` true (the default) anyone can call it.
- Otherwise the request needs the same `Api-Key` header as the Delivery API, and gets `401` without it.

It is served even when the Delivery API itself is switched off, and it needs no backoffice user.

### Who the member is

Send the member's Delivery API token and flags are checked for that member, so their member groups and audiences
apply:

```
Authorization: Bearer <member access token>
```

This needs the Delivery API's member authorization to be switched on (`MemberAuthorization` in the Delivery API
settings). Without a token, or with one that isn't a member's, the request is a visitor who isn't signed in: percentage
rollouts are off for them, as everywhere else in Feature Flags. A member signed in with the cookie is recognised too.
The same lookup is used for every request in the site, not only this endpoint.

A member token is recognised on this endpoint and on the other `/umbraco/` paths, because that is where Umbraco's
token handling runs. On a page of your own site (outside `/umbraco/`) a bearer token may not be recognised, so those
pages know a member by the member cookie.

### Which site

Send the `Start-Item` header, as you do to the Delivery API, to place the request in a root, so a
[Root condition](conditions.md) can match:

```
Start-Item: uk-shop
```

It takes a root's URL segment or key.

### A fetch example

```ts
const response = await fetch(`${umbracoUrl}/umbraco/umbriciansfeatureflags/delivery/v1/flags`, {
  headers: {
    'Api-Key': process.env.UMBRACO_API_KEY!, // only when the Delivery API isn't public
    'Start-Item': 'uk-shop',
    Authorization: `Bearer ${memberToken}`, // only for a signed-in member
  },
});
const flags: Record<string, boolean> = await response.json();
if (flags['Checkout.ExpressPay']) { /* show it */ }
```

Call it from your server, not the browser, when it needs an `Api-Key`: the key must stay secret.

## Caching

The answer is always `Cache-Control: private, no-store`. It differs by member, root and moment, so no shared cache,
CDN or browser may keep it. Cache it yourself, per member, if you need to.

## Why flags aren't in content responses

Delivery API content responses are cached and the same for everyone. A flag that depends on who is asking can't be in
them without either leaking one member's answer to another or making every response uncacheable. So flags come
from this endpoint, which is never cached.
