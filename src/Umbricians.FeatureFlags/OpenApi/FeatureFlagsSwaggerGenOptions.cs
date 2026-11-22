using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Umbricians.FeatureFlags.OpenApi;

/// <summary>
/// Adds the package's Management API document to the backoffice's Swagger UI, signed in as the backoffice user.
/// </summary>
internal sealed class FeatureFlagsSwaggerGenOptions : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc(Constants.ApiName, new OpenApiInfo { Title = "Umbricians Feature Flags", Version = "1.0" });

        // A condition's parameters are free-form JSON; without this Swashbuckle describes JsonObject's own members.
        options.MapType<JsonObject>(() => new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            AdditionalProperties = new OpenApiSchema(),
        });
        options.OperationFilter<FeatureFlagsSecurityRequirementsOperationFilter>();
    }
}
