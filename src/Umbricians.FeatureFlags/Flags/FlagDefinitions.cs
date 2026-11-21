using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.FeatureManagement;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// Converts between Microsoft's flag definitions and the <see cref="FlagSettings"/> the backoffice edits.
/// </summary>
/// <remarks>
/// Overrides are stored as Microsoft's <c>feature_management</c> JSON and read back with Microsoft's own
/// <see cref="ConfigurationFeatureDefinitionProvider"/>, so they mean exactly what they would in appsettings. JSON is
/// only ever written from <see cref="FlagSettings"/>, never rebuilt from a parsed definition, which would lose its value
/// types.
/// </remarks>
internal static class FlagDefinitions
{
    public const string TargetingFilter = "Microsoft.Targeting";
    private const string TargetingFilterShortName = "Targeting";
    private const string AlwaysOnFilter = "AlwaysOn";
    private const string OnFilter = "On";
    private const string VariantsReason = "It has variants, which the backoffice can't edit yet.";

    /// <summary>
    /// The settings a definition amounts to, or null with the reason when it uses more than the backoffice can edit:
    /// any filter but one targeting filter, a targeting filter with percentages or exclusions, or variants.
    /// </summary>
    public static FlagSettings? ToSettings(FeatureDefinition definition, out string? notEditableReason)
    {
        notEditableReason = null;
        if (definition.Variants?.Any() == true || definition.Allocation is not null)
        {
            notEditableReason = VariantsReason;
            return null;
        }

        FeatureFilterConfiguration[] filters = definition.EnabledFor?.ToArray() ?? [];
        bool enabled = definition.Status != FeatureStatus.Disabled && filters.Length > 0;

        if (filters.Length == 0)
        {
            return FlagSettings.Off;
        }

        if (filters.Length == 1 && IsAlwaysOn(filters[0].Name))
        {
            return new FlagSettings(enabled, null);
        }

        return FromFilters(enabled, filters.Select(filter => filter.Name).ToArray(), () => filters[0].Parameters, out notEditableReason);
    }

    /// <summary>
    /// The settings stored override JSON amounts to, read from the JSON itself: Microsoft drops the conditions of a
    /// flag that isn't enabled, which would lose the audience an editor switched off. Null, with the reason, for JSON
    /// that uses more than the backoffice can edit or can't be read.
    /// </summary>
    public static FlagSettings? ReadOverride(string json, out string? notEditableReason)
    {
        notEditableReason = null;
        IConfiguration flag;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject)
            {
                throw new JsonException("The definition is not a JSON object.");
            }

            flag = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidDataException)
        {
            notEditableReason = "Its saved setting can't be read.";
            return null;
        }

        if (flag.GetSection("variants").GetChildren().Any() || flag.GetSection("allocation").Exists())
        {
            notEditableReason = VariantsReason;
            return null;
        }

        bool enabled = bool.TryParse(flag["enabled"], out bool value) && value;
        IConfigurationSection[] filters = flag.GetSection("conditions:client_filters").GetChildren().ToArray();
        if (filters.Length == 0)
        {
            return new FlagSettings(enabled, null);
        }

        return FromFilters(enabled, filters.Select(filter => filter["name"]).ToArray(), () => filters[0].GetSection("parameters"), out notEditableReason);
    }

    /// <summary>A flag's settings as Microsoft's <c>feature_management</c> JSON, for storing as an override.</summary>
    public static string ToJson(string flagName, FlagSettings settings)
    {
        var flag = new JsonObject
        {
            ["id"] = flagName,
            ["enabled"] = settings.Enabled,
        };

        // With no audience the flag has no conditions, which Microsoft's schema reads as on for everyone when enabled.
        if (settings.Audience is not null)
        {
            var groups = new JsonArray();
            foreach (string group in settings.Audience.Groups)
            {
                groups.Add(new JsonObject { ["Name"] = group, ["RolloutPercentage"] = 100 });
            }

            flag["conditions"] = new JsonObject
            {
                ["client_filters"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = TargetingFilter,
                        ["parameters"] = new JsonObject
                        {
                            ["Audience"] = new JsonObject
                            {
                                ["Users"] = new JsonArray(settings.Audience.Members.Select(m => (JsonNode?)m).ToArray()),
                                ["Groups"] = groups,
                                ["DefaultRolloutPercentage"] = 0,
                            },
                        },
                    },
                },
            };
        }

        return flag.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Reads stored overrides with Microsoft's own provider, keyed by flag name. An override whose JSON can't be read is
    /// left out, so its flag falls back to its default.
    /// </summary>
    public static async Task<Dictionary<string, FeatureDefinition>> ParseAsync(
        IEnumerable<(string FlagName, string Json)> overrides, Action<string, Exception>? onInvalid = null)
    {
        var flags = new JsonArray();
        var names = new List<string>();
        foreach ((string flagName, string json) in overrides)
        {
            try
            {
                JsonNode? node = JsonNode.Parse(json);
                if (node is not JsonObject flag)
                {
                    throw new JsonException("The definition is not a JSON object.");
                }

                // The stored name is the one that counts, whatever the JSON says.
                flag["id"] = flagName;
                flags.Add(flag);
                names.Add(flagName);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                onInvalid?.Invoke(flagName, exception);
            }
        }

        var document = new JsonObject { ["feature_management"] = new JsonObject { ["feature_flags"] = flags } };
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(document.ToJsonString())))
            .Build();
        var provider = new ConfigurationFeatureDefinitionProvider(configuration);

        var definitions = new Dictionary<string, FeatureDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            try
            {
                FeatureDefinition? definition = await provider.GetFeatureDefinitionAsync(name);
                if (definition is not null)
                {
                    definitions[name] = definition;
                }
            }
            catch (Exception exception) when (exception is FeatureManagementException or InvalidOperationException or FormatException)
            {
                onInvalid?.Invoke(name, exception);
            }
        }

        return definitions;
    }

    /// <summary>A definition that is off, or on for everyone, as code declares it.</summary>
    public static FeatureDefinition Simple(string flagName, bool enabled) => enabled
        ? new FeatureDefinition { Name = flagName, EnabledFor = [new FeatureFilterConfiguration { Name = AlwaysOnFilter }] }
        : new FeatureDefinition { Name = flagName, Status = FeatureStatus.Disabled, EnabledFor = [] };

    private static bool IsAlwaysOn(string? name) =>
        string.Equals(name, AlwaysOnFilter, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, OnFilter, StringComparison.OrdinalIgnoreCase);

    private static bool IsTargeting(string? name) =>
        string.Equals(name, TargetingFilter, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, TargetingFilterShortName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The settings for a flag whose only filter is targeting, read from its parameters; otherwise null, with the reason.
    /// Off and on-for-everyone are worked out before this, since a definition and stored JSON spell them differently.
    /// </summary>
    private static FlagSettings? FromFilters(bool enabled, string?[] filterNames, Func<IConfiguration?> targetingParameters, out string? notEditableReason)
    {
        if (filterNames.Length == 1 && IsTargeting(filterNames[0]))
        {
            FlagAudience? audience = ReadAudience(targetingParameters(), out notEditableReason);
            return audience is null ? null : new FlagSettings(enabled, audience);
        }

        notEditableReason = filterNames.Length == 1
            ? $"It uses the '{filterNames[0]}' filter, which the backoffice can't edit yet."
            : "It combines several filters, which the backoffice can't edit yet.";
        return null;
    }

    private static FlagAudience? ReadAudience(IConfiguration? parameters, out string? notEditableReason)
    {
        notEditableReason = null;
        IConfigurationSection audience = parameters?.GetSection("Audience") ?? EmptySection();

        if (Percentage(audience["DefaultRolloutPercentage"]) is not (null or 0))
        {
            notEditableReason = "It is on for a percentage of everyone, which the backoffice can't edit yet.";
            return null;
        }

        IConfigurationSection exclusion = audience.GetSection("Exclusion");
        if (exclusion.GetSection("Users").GetChildren().Any() || exclusion.GetSection("Groups").GetChildren().Any())
        {
            notEditableReason = "It excludes members or groups, which the backoffice can't edit yet.";
            return null;
        }

        var groups = new List<string>();
        foreach (IConfigurationSection group in audience.GetSection("Groups").GetChildren())
        {
            if (Percentage(group["RolloutPercentage"]) is not (null or 100))
            {
                notEditableReason = "It is on for a percentage of a group, which the backoffice can't edit yet.";
                return null;
            }

            if (!string.IsNullOrWhiteSpace(group["Name"]))
            {
                groups.Add(group["Name"]!);
            }
        }

        string[] members = audience.GetSection("Users").GetChildren()
            .Select(user => user.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

        return new FlagAudience(members, groups);
    }

    private static double? Percentage(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number)
            ? number
            : null;

    private static IConfigurationSection EmptySection() => new ConfigurationBuilder().Build().GetSection("Audience");
}
