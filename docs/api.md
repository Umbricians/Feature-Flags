# Calling the API from scripts

Feature Flags has a Management API under `/umbraco/umbriciansfeatureflags/api/v1/`. The backoffice uses it, and so
can your scripts and CI pipelines. It is listed as `umbricians-feature-flags` in the backoffice's Swagger UI.

## Who can call it

- **The backoffice**, signed in as an editor.
- **An Umbraco API user** with client credentials, in a user group that has the Feature Flags permissions: **View
  feature flags** to read, and **Change feature flags** as well to save, switch or reset. An API user is a user without
  a password, so the permission check is the same as for an editor, and a change is credited to the API user.

There are no API keys or sign-in of the package's own.

To make an API user, go to Users, choose Create API user, add it to a user group with the permissions above, then
create its client credentials. Umbraco's [API users](https://docs.umbraco.com/umbraco-cms/fundamentals/data/users/api-users)
page walks through it. The client id starts with `umbraco-back-office-`.

## Getting a token

```bash
SITE=https://example.com
TOKEN=$(curl -s "$SITE/umbraco/management/api/v1/security/back-office/token" \
  -d grant_type=client_credentials \
  -d client_id=umbraco-back-office-deploy \
  -d client_secret="$FLAGS_SECRET" | jq -r .access_token)
```

Tokens expire, so ask for one at the start of each script. Call it over HTTPS.

## Examples

Flag names go in the query string, never the path, because they may hold dots, slashes and spaces. Use
`--data-urlencode` or encode them yourself.

List flags (a page of at most 100):

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$SITE/umbraco/umbriciansfeatureflags/api/v1/flags?take=50&state=audience&orderBy=name"
```

Get one flag:

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$SITE/umbraco/umbriciansfeatureflags/api/v1/flag?name=Checkout.ExpressPay"
```

Save a flag's settings: on for an audience, which is Microsoft's `Microsoft.Targeting` condition. `match` is `any` (the
default) or `all`, and only matters with two or more conditions. A flag with no conditions is on for everyone.

```bash
curl -s -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  "$SITE/umbraco/umbriciansfeatureflags/api/v1/flag?name=Checkout.ExpressPay" \
  -d '{
    "enabled": true,
    "match": "any",
    "conditions": [
      { "name": "Microsoft.Targeting",
        "parameters": { "Audience": { "Users": [], "Groups": [ { "Name": "Beta testers", "RolloutPercentage": 100 } ], "DefaultRolloutPercentage": 0 } } }
    ]
  }'
```

Switch a flag on or off, keeping its conditions:

```bash
curl -s -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  "$SITE/umbraco/umbriciansfeatureflags/api/v1/flag/enabled?name=Checkout.ExpressPay" -d '{ "enabled": false }'
```

Reset a flag to its default, or remove the setting of a flag nobody declares any more:

```bash
curl -s -X DELETE -H "Authorization: Bearer $TOKEN" "$SITE/umbraco/umbriciansfeatureflags/api/v1/flag?name=Checkout.ExpressPay"
```

## Endpoints

| Endpoint | |
| --- | --- |
| `GET flags?skip&take&filter&state&show&marks&section&tag&orderBy` | A page of flags (at most 100), searched, filtered and sorted across every flag, as `{ total, items }`. `state` is `off`, `everyone`, `audience`, `conditions` or `custom`. `show` is `declared` (the default) or `undeclared`; `marks` narrows to flags with every one of `changed`, `differs` and `twice`. `section` is a section's name, or empty for flags in no section; leave it out for every section. `orderBy` is `name` (the default), `section` or `updated`. |
| `GET flags/summary` | The sections and tags in use, counts, whether overrides are read-only, and the environment's name. |
| `GET flag?name=` | One flag. |
| `GET provider-groups` | The groups the site's group providers offer. |
| `PUT flag?name=` | Save a flag's settings. Body: `{ "enabled": true, "conditions": [ { "name": "...", "parameters": { } } ], "match": "any" }`. Returns the flag as it is now. |
| `PUT flag/enabled?name=` | Switch a flag on or off, keeping its conditions. Body: `{ "enabled": true }`. Returns the flag. |
| `DELETE flag?name=` | Reset a flag to its default, or remove the setting of a flag nobody declares any more. Returns the flag; for a removed flag there is nothing left to return, so the body is empty. |

A flag has every field of `FeatureFlag` in C#, in camelCase: `name`, `isDeclared`, `declaredIn`, `declaredTwice`,
`section`, `description`, `tags`, `isPublic`, `state`, `defaultState`, `settings` and `defaultSettings`
(`{ enabled, conditions, match }`, null when the flag uses more than settings can say), `override`
(`{ definition, updatedBy, updatedUtc }`), `differsFromDefault`, `actions` (`switch`, `save`, `reset` and `remove`, each
`{ allowed, reason }`) and `summary`. The backoffice's own fields come after those: `enabled`, `audience` and
`audienceDetail`, and `override.updatedByName`.

## Status codes

| Code | When |
| --- | --- |
| 200 | Done. |
| 400 | The request is wrong: a page or filter value the API doesn't know, a `match` other than `any` or `all`, or a body that isn't the shape above. |
| 401 | No token, or an expired one. |
| 403 | The user's groups lack the View or Change permission. |
| 404 | Nobody declares the flag (a save, switch or reset); `GET flag` also 404s for a name with no setting. |
| 409 | The rules refused the change, such as a read-only site, or a flag the backoffice can't edit. `detail` says why. When conditions are invalid or unregistered, `problems` lists each problem. When a handler of the Feature Flags notifications cancelled the change, `messages` holds what it said. |

Errors are [problem details](https://www.rfc-editor.org/rfc/rfc9457) (`title`, `detail`, `status`), with `problems`
or `messages` as extra fields on a 409.

## What is promised for v1

Every endpoint, every field of the responses and bodies above, every query parameter, and every status code stays as it
is for the whole of 1.x. New fields, endpoints and values may be added, so ignore fields you don't know.

Not promised: the wording of `title`, `detail`, `reason`, `problems`, `messages` and `summary`, which are for people, and
the order of items, except what `orderBy` gives you.

## Versioning

The `v1` in the path follows the package's major version. A breaking change ships in 2.0 as `/api/v2/`, and 2.0 drops
`v1`.

## Good to know

- **Last save wins.** There is no `ETag` or `If-Match`: a save replaces what is there, whoever saved it last.
- **No bulk endpoint.** To change many flags, call `PUT flag` once for each.
- A save replaces all of a flag's conditions, so send the ones you want to keep as well as the ones you change. Read
  the flag first with `GET flag`.
- Changes made through the API publish the same [notifications](notifications.md) as any other, with the cause `Editor`.
