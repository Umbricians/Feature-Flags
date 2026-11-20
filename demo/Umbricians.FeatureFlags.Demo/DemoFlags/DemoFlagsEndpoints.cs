using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Security;

namespace Umbricians.FeatureFlags.Demo.DemoFlags;

/// <summary>
/// A page at /flags that shows which flags are on for whoever is signed in, with buttons to sign in as each test
/// member. Demo only, and only when the Development settings give the test members a password.
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

            MemberIdentityUser? member = await members.GetCurrentMemberAsync();
            var rows = new StringBuilder();
            await foreach (string name in features.GetFeatureNamesAsync())
            {
                bool on = await features.IsEnabledAsync(name);
                rows.Append($"<tr><td>{Encode(name)}</td><td class=\"{(on ? "on" : "off")}\">{(on ? "On" : "Off")}</td></tr>");
            }

            var accounts = new StringBuilder();
            foreach (DemoMemberAccount account in options.Value.Accounts)
            {
                string groups = account.Groups.Count == 0 ? "no member groups" : string.Join(", ", account.Groups);
                accounts.Append($"""
                    <form method="post" action="{Path}/sign-in"><input type="hidden" name="email" value="{Encode(account.Email)}">
                    <button type="submit">Sign in as {Encode(account.Name)}</button> <small>{Encode(account.Email)} · {Encode(groups)}</small></form>
                    """);
            }

            string who = member is null
                ? "a visitor who isn't signed in"
                : $"{Encode(member.Name ?? member.Email ?? member.UserName ?? "a member")} ({Encode(member.Email ?? "")})";
            string html = $$"""
                <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Feature Flags demo</title>
                <style>body{font-family:system-ui,sans-serif;max-width:720px;margin:40px auto;padding:0 16px}
                table{border-collapse:collapse;width:100%}td{padding:6px 8px;border-bottom:1px solid #ddd}
                .on{color:#0b5c36;font-weight:bold}.off{color:#666}form{margin:6px 0}small{color:#666}</style></head>
                <body><h1>Feature Flags demo</h1>
                <p>Flags as Microsoft.FeatureManagement sees them for <strong>{{who}}</strong>. Change them in the backoffice
                under Settings → Feature Flags, then reload this page.</p>
                <table>{{rows}}</table>
                <h2>Sign in as a test member</h2>{{accounts}}
                <form method="post" action="{{Path}}/sign-out"><button type="submit">Sign out</button></form>
                </body></html>
                """;
            return Results.Content(html, "text/html");
        });

        endpoints.MapPost($"{Path}/sign-in", async (HttpContext http, IMemberSignInManager signIn, IOptions<DemoMembersOptions> options) =>
        {
            string? email = http.Request.Form["email"];
            if (string.IsNullOrEmpty(options.Value.Password) || options.Value.Accounts.All(a => a.Email != email))
            {
                return Results.NotFound();
            }

            await signIn.PasswordSignInAsync(email!, options.Value.Password, isPersistent: false, lockoutOnFailure: false);
            return Results.Redirect(Path);
        }).DisableAntiforgery();

        endpoints.MapPost($"{Path}/sign-out", async (IMemberSignInManager signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.Redirect(Path);
        }).DisableAntiforgery();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
