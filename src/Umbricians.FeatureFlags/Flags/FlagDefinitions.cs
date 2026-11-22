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
    public const string TargetingFilter = FlagSettings.TargetingName;
    private const string AlwaysOnFilter = "AlwaysOn";
    private const string OnFilter = "On";
    private const string VariantsReason = "It has variants, which the backoffice can't edit yet.";

    /// <summary>
    /// The settings a definition amounts to, with every filter read as a condition. The reason is set when the backoffice
    /// can't edit them: anything but no conditions or one targeting filter with members and groups. Null settings, with
    /// the reason, for variants.
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
            return new FlagSettings(enabled, []);
        }

        var settings = new FlagSettings(
            enabled,
            filters.Select(filter => new FlagCondition(filter.Name ?? "", ToJson(filter.Parameters) as JsonObject ?? new JsonObject())).ToArray(),
            definition.RequirementType == RequirementType.All ? ConditionMatch.All : ConditionMatch.Any);
        notEditableReason = NotEditableReason(settings);
        return settings;
    }

    /// <summary>
    /// The settings stored override JSON amounts to, read from the JSON itself: Microsoft drops the conditions of a
    /// flag that isn't enabled, which would lose the audience an editor switched off. Every filter is kept as stored.
    /// Null, with the reason, for JSON that has variants or can't be read.
    /// </summary>
    public static FlagSettings? ReadOverride(string json, out string? notEditableReason)
    {
        notEditableReason = null;
        JsonObject flag;
        try
        {
            flag = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("The definition is not a JSON object.");
        }
        catch (JsonException)
        {
            notEditableReason = "Its saved setting can't be read.";
            return null;
        }

        if (Property(flag, "variants") is JsonArray { Count: > 0 } || Property(flag, "allocation") is not null)
        {
            notEditableReason = VariantsReason;
            return null;
        }

        bool enabled = Property(flag, "enabled") is JsonValue flagged
            && (flagged.TryGetValue(out bool on) ? on : flagged.TryGetValue(out string? text) && bool.TryParse(text, out on) && on);

        var conditions = new List<FlagCondition>();
        JsonObject? block = Property(flag, "conditions") as JsonObject;
        if (Property(block, "client_filters") is JsonArray filters)
        {
            foreach (JsonObject filter in filters.OfType<JsonObject>())
            {
                string name = Property(filter, "name") is JsonValue nameValue && nameValue.TryGetValue(out string? n) ? n : "";
                conditions.Add(new FlagCondition(name, (Property(filter, "parameters") as JsonObject)?.DeepClone() as JsonObject ?? new JsonObject()));
            }
        }

        ConditionMatch match = Property(block, "requirement_type") is JsonValue requirement
            && requirement.TryGetValue(out string? type)
            && string.Equals(type, "All", StringComparison.OrdinalIgnoreCase)
                ? ConditionMatch.All
                : ConditionMatch.Any;

        var settings = new FlagSettings(enabled, conditions, match);
        notEditableReason = NotEditableReason(settings);
        return settings;
    }

    /// <summary>A flag's settings as Microsoft's <c>feature_management</c> JSON, for storing as an override.</summary>
    public static string ToJson(string flagName, FlagSettings settings)
    {
        var flag = new JsonObject
        {
            ["id"] = flagName,
            ["enabled"] = settings.Enabled,
        };

        // With no conditions the flag has none to write, which Microsoft's schema reads as on for everyone when enabled.
        if (settings.Conditions.Count > 0)
        {
            var filters = new JsonArray();
            foreach (FlagCondition condition in settings.Conditions)
            {
                var filter = new JsonObject { ["name"] = condition.Name };
                if (condition.Parameters.Count > 0)
                {
                    filter["parameters"] = condition.Parameters.DeepClone();
                }

                filters.Add(filter);
            }

            flag["conditions"] = new JsonObject
            {
                ["requirement_type"] = settings.Match == ConditionMatch.All ? "All" : "Any",
                ["client_filters"] = filters,
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

    /// <summary>
    /// Why the backoffice can't edit these settings, or null when it can: no conditions, or one targeting condition with
    /// only members and groups.
    /// </summary>
    private static string? NotEditableReason(FlagSettings settings)
    {
        if (settings.Conditions.Count == 0)
        {
            return null;
        }

        if (settings.Conditions.Count > 1)
        {
            return "It combines several filters, which the backoffice can't edit yet.";
        }

        FlagCondition condition = settings.Conditions[0];
        if (!FlagSettings.IsTargeting(condition.Name))
        {
            return $"It uses the '{condition.Name}' filter, which the backoffice can't edit yet.";
        }

        FlagAudience.TryRead(condition.Parameters, out string? reason);
        return reason;
    }

    private static JsonNode? Property(JsonObject? parent, string name) =>
        parent?.FirstOrDefault(property => string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>
    /// Configuration as JSON: a section with numbered children is an array, one with named children an object, and a
    /// value is a number or true or false when it reads as one, since configuration holds everything as text.
    /// </summary>
    private static JsonNode? ToJson(IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return null;
        }

        IConfigurationSection[] children = configuration.GetChildren().ToArray();
        if (children.Length == 0)
        {
            return configuration is IConfigurationSection { Value: { } value } ? Scalar(value) : null;
        }

        if (children.Select((child, index) => child.Key == index.ToString(System.Globalization.CultureInfo.InvariantCulture)).All(isIndex => isIndex))
        {
            return new JsonArray(children.Select(child => ToJson(child)).ToArray());
        }

        var result = new JsonObject();
        foreach (IConfigurationSection child in children)
        {
            result[child.Key] = ToJson(child);
        }

        return result;
    }

    private static JsonNode Scalar(string value) =>
        bool.TryParse(value, out bool boolean) ? JsonValue.Create(boolean)
        : long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long integer) ? JsonValue.Create(integer)
        : double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number) ? JsonValue.Create(number)
        : JsonValue.Create(value)!;
}
