using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Security;

namespace Umbricians.FeatureFlags.Demo.DemoFlags;

/// <summary>
/// The demo page at /flags: a pretend shop at three-quarter scale, whose parts flags switch, beside a panel to change
/// who is signed in and see which flags are on for them. Demo only, and only when the Development settings give the
/// test members a password.
/// </summary>
public static class DemoFlagsEndpoints
{
    public const string Path = "/flags";

    public static void MapDemoFlags(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, async (HttpContext http, IFeatureManagerSnapshot features, IMemberManager members, IOptions<DemoMembersOptions> options) =>
        {
            if (string.IsNullOrEmpty(options.Value.Password))
            {
                return Results.NotFound();
            }

            bool outline = http.Request.Query["outline"] == "1";
            MemberIdentityUser? member = await members.GetCurrentMemberAsync();

            var flags = new StringBuilder();
            await foreach (string name in features.GetFeatureNamesAsync())
            {
                bool on = await features.IsEnabledAsync(name);
                flags.Append($"""<li><span>{Encode(name)}</span><b class="{(on ? "on" : "off")}">{(on ? "On" : "Off")}</b></li>""");
            }

            var people = new StringBuilder();
            people.Append(Person(null, "A visitor who isn't signed in", "Not signed in", member is null, outline));
            foreach (DemoMemberAccount account in options.Value.Accounts)
            {
                string groups = account.Groups.Count == 0 ? "No member groups" : string.Join(", ", account.Groups);
                bool current = string.Equals(member?.Email, account.Email, StringComparison.OrdinalIgnoreCase);
                people.Append(Person(account.Email, account.Name, $"{account.Email} · {groups}", current, outline));
            }

            string html = $$"""
                <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Feature Flags demo</title>
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <style>
                *{box-sizing:border-box}body{margin:0;font-family:system-ui,-apple-system,"Segoe UI",sans-serif;background:#e9ebf2;color:#1b264f;height:100vh;display:flex}
                .stage{flex:1;min-width:0;padding:24px;display:flex;flex-direction:column}
                .browser{flex:1;display:flex;flex-direction:column;background:#fff;border-radius:12px;box-shadow:0 10px 30px rgba(27,38,79,.15);overflow:hidden}
                .chrome{display:flex;align-items:center;gap:8px;padding:10px 14px;background:#f3f4f8;border-bottom:1px solid #dde0ea}
                .chrome i{width:11px;height:11px;border-radius:50%;background:#d5d8e2}
                .chrome span{flex:1;margin-left:10px;background:#fff;border-radius:6px;padding:5px 12px;font-size:12px;color:#5e5e66}
                .viewport{flex:1;position:relative;overflow:hidden}
                .viewport iframe{position:absolute;top:0;left:0;width:133.334%;height:133.334%;transform:scale(.75);transform-origin:0 0;border:0}
                .scale{font-size:12px;color:#5e5e66;margin-top:8px;text-align:center}
                aside{width:340px;flex-shrink:0;background:#fff;border-left:1px solid #dde0ea;padding:24px;overflow-y:auto}
                h1{font-size:18px;margin:0 0 6px}h2{font-size:13px;text-transform:uppercase;letter-spacing:.04em;color:#5e5e66;margin:24px 0 10px}
                p{font-size:14px;line-height:1.5;color:#3c3c42;margin:0}
                form{margin:0}button.person{display:block;width:100%;text-align:left;background:#fff;border:1px solid #dde0ea;border-radius:8px;padding:10px 12px;margin-bottom:8px;cursor:pointer;font:inherit;color:inherit}
                button.person.current{border-color:#3544b1;background:#eef0fb;box-shadow:inset 3px 0 0 #3544b1}
                button.person strong{display:block;font-size:14px}button.person small{color:#5e5e66;font-size:12px}
                label.toggle{display:flex;gap:8px;align-items:center;font-size:14px;cursor:pointer}
                ul{list-style:none;margin:0;padding:0}li{display:flex;justify-content:space-between;gap:8px;padding:7px 0;border-bottom:1px solid #eef0f4;font-size:13px}
                li span{font-family:ui-monospace,Menlo,monospace;word-break:break-all}b.on{color:#0b5c36}b.off{color:#8a8a93}
                a.backoffice{display:block;margin-top:16px;text-align:center;background:#1b264f;color:#fff;border-radius:8px;padding:10px;text-decoration:none;font-size:14px;font-weight:600}
                </style></head>
                <body>
                <div class="stage">
                  <div class="browser">
                    <div class="chrome"><i></i><i></i><i></i><span>ridgeline.example/jackets/trail-shell-jacket</span></div>
                    <div class="viewport"><iframe src="{{Path}}/shop?outline={{(outline ? "1" : "0")}}" title="Example shop page"></iframe></div>
                  </div>
                  <div class="scale">An example product page, shown at 75%. Each flag switches one part of it, not the whole page.</div>
                </div>
                <aside>
                  <h1>Feature Flags demo</h1>
                  <p>See how one page changes for different people as flags are switched in the backoffice: a button here, a search box there.</p>
                  <h2>Viewing as</h2>
                  {{people}}
                  <h2>Page</h2>
                  <label class="toggle"><input type="checkbox" {{(outline ? "checked" : "")}} onchange="location.href='{{Path}}?outline='+(this.checked?'1':'0')"> Show flagged areas</label>
                  <h2>Flags for this visitor</h2>
                  <ul>{{flags}}</ul>
                  <a class="backoffice" href="/umbraco/section/settings/workspace/umbricians-feature-flags-root" target="_blank" rel="noopener">Change flags in the backoffice</a>
                  <p style="margin-top:10px;font-size:12px;color:#5e5e66">After changing a flag, reload this page to see the result.</p>
                </aside>
                </body></html>
                """;
            return Results.Content(html, "text/html");
        });

        endpoints.MapPost($"{Path}/sign-in", async (HttpContext http, IMemberSignInManager signIn, IOptions<DemoMembersOptions> options) =>
        {
            string? email = http.Request.Form["email"];
            string outline = http.Request.Form["outline"] == "1" ? "1" : "0";
            if (string.IsNullOrEmpty(options.Value.Password))
            {
                return Results.NotFound();
            }

            await signIn.SignOutAsync();
            if (!string.IsNullOrEmpty(email))
            {
                if (options.Value.Accounts.All(a => a.Email != email))
                {
                    return Results.NotFound();
                }

                await signIn.PasswordSignInAsync(email, options.Value.Password, isPersistent: false, lockoutOnFailure: false);
            }

            return Results.Redirect($"{Path}?outline={outline}");
        }).DisableAntiforgery();
    }

    /// <summary>A button that signs in as a test member, or signs out when <paramref name="email"/> is null.</summary>
    private static string Person(string? email, string name, string detail, bool current, bool outline) => $"""
        <form method="post" action="{Path}/sign-in"><input type="hidden" name="email" value="{Encode(email ?? "")}">
        <input type="hidden" name="outline" value="{(outline ? "1" : "0")}">
        <button class="person{(current ? " current" : "")}" type="submit" aria-pressed="{(current ? "true" : "false")}"><strong>{Encode(name)}</strong><small>{Encode(detail)}</small></button></form>
        """;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
