using System.Globalization;
using System.Text.Json.Nodes;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// The package's own condition: a window of time, evaluated by Microsoft's time window filter. Either end may be left
/// out. Times are read the way Microsoft reads them (ISO 8601 and RFC 1123 both work), and the backoffice writes ISO 8601
/// in UTC. A window that repeats can be shown but not edited.
/// </summary>
internal sealed class TimeWindowCondition : IFeatureFlagCondition
{
    internal const string FilterName = "Microsoft.TimeWindow";

    internal const string RepeatingReason = "Repeating windows can't be edited yet.";

    public string Name => FilterName;

    public string DisplayName => "Time window";

    public IEnumerable<string> Validate(JsonObject parameters)
    {
        var problems = new List<string>();
        if (HasRecurrence(parameters))
        {
            problems.Add(RepeatingReason);
        }

        DateTimeOffset? start = ReadEnd(parameters, "Start", "start", problems, out bool hasStart);
        DateTimeOffset? end = ReadEnd(parameters, "End", "end", problems, out bool hasEnd);
        if (!hasStart && !hasEnd && !HasRecurrence(parameters))
        {
            problems.Add("Choose when it starts, when it ends, or both.");
        }

        if (start is { } from && end is { } until && from >= until)
        {
            problems.Add("It must start before it ends.");
        }

        return problems;
    }

    public string Describe(JsonObject parameters)
    {
        string repeats = HasRecurrence(parameters) ? " (repeats)" : "";
        DateTimeOffset? start = ReadEnd(parameters, "Start", "start", [], out _);
        DateTimeOffset? end = ReadEnd(parameters, "End", "end", [], out _);
        return (start, end) switch
        {
            ({ } from, { } until) => $"From {Format(from)} until {Format(until)}{repeats}",
            ({ } from, null) => $"From {Format(from)}{repeats}",
            (null, { } until) => $"Until {Format(until)}{repeats}",
            _ => $"A time window that can't be read{repeats}",
        };
    }

    /// <summary>Why the backoffice can't edit these parameters, or null when it can: only a repeating window can't be.</summary>
    internal static string? NotEditableReason(JsonObject parameters) => HasRecurrence(parameters) ? RepeatingReason : null;

    private static bool HasRecurrence(JsonObject parameters) => Find(parameters, "Recurrence") is not null;

    private static JsonNode? Find(JsonObject parameters, string key) =>
        parameters.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    // Microsoft binds Start and End to DateTimeOffset, which parses with the invariant culture: ISO 8601, RFC 1123 and more.
    private static DateTimeOffset? ReadEnd(JsonObject parameters, string key, string word, List<string> problems, out bool present)
    {
        present = false;
        JsonNode? node = Find(parameters, key);
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue(out string? text))
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            present = true;
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed))
            {
                return parsed;
            }

            problems.Add($"The {word} '{text}' isn't a date and time it can read.");
            return null;
        }

        present = true;
        problems.Add($"The {word} isn't a date and time.");
        return null;
    }

    private static string Format(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC";
}
