using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Extensions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .AddFeatureFlags(flags => flags.Declare("Tests.InCode", description: "Declared in code."))
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();

        // Says whether a flag is on for the request, so the tests can check conditions that look at the request.
        u.EndpointRouteBuilder.MapGet("/test-flags/{name}", async (string name, IFeatureManager features) =>
            await features.IsEnabledAsync(name) ? "on" : "off");
    });

await app.RunAsync();

/// <summary>Lets the integration tests start the site.</summary>
public partial class Program;
