#if UMBRACO_18_OR_GREATER
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Api.Management.OpenApi;
using Umbraco.Cms.Core.DependencyInjection;

namespace Umbricians.FeatureFlags.OpenApi;

/// <summary>
/// The package's API documents on Umbraco 18 and later, which use Microsoft.AspNetCore.OpenApi.
/// </summary>
internal static class FeatureFlagsOpenApi
{
    /// <summary>
    /// Adds the package's Management API document, signed in as the backoffice user, and its Delivery document.
    /// </summary>
    public static IUmbracoBuilder AddFeatureFlagsOpenApi(this IUmbracoBuilder builder)
    {
        builder.AddBackOfficeOpenApiDocument(Constants.ApiName, document => document
            .WithTitle("Umbricians Feature Flags")
            .WithBackOfficeAuthentication()
            .ConfigureOpenApiOptions(options => options.AddSchemaTransformer((schema, context, _) =>
            {
                // A condition's parameters are free-form JSON, not JsonObject's own members.
                if (context.JsonTypeInfo.Type == typeof(JsonObject))
                {
                    schema.Type = JsonSchemaType.Object;
                    schema.Properties?.Clear();
                    schema.AdditionalProperties = new OpenApiSchema();
                }

                return Task.CompletedTask;
            })));
        builder.AddBackOfficeOpenApiDocument(Constants.DeliveryApiName, document => document
            .WithTitle("Umbricians Feature Flags Delivery"));
        return builder;
    }
}
#endif
