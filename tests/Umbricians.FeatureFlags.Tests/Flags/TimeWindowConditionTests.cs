using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Tests.Flags;

/// <summary>The time window condition: what it accepts, how it reads, and that Microsoft's own filter agrees with it.</summary>
public class TimeWindowConditionTests
{
    private const string Declared = """{ "feature_management": { "feature_flags": [ { "id": "Sale", "enabled": true } ] } }""";

    private static JsonObject Window(string? start, string? end, JsonNode? recurrence = null)
    {
        var parameters = new JsonObject();
        if (start is not null)
        {
            parameters["Start"] = start;
        }

        if (end is not null)
        {
            parameters["End"] = end;
        }

        if (recurrence is not null)
        {
            parameters["Recurrence"] = recurrence;
        }

        return parameters;
    }

    private static JsonNode Recurrence() => JsonNode.Parse("""{ "Pattern": { "Type": "Daily" }, "Range": { "Type": "NoEnd" } }""")!;

    private static readonly TimeWindowCondition Condition = new();

    [Test]
    public void Either_end_can_be_left_out()
    {
        Assert.That(Condition.Validate(Window("2026-12-01T09:00:00Z", null)), Is.Empty);
        Assert.That(Condition.Validate(Window(null, "2026-12-31T23:59:00Z")), Is.Empty);
        Assert.That(Condition.Validate(Window("2026-12-01T09:00:00Z", "2026-12-31T23:59:00Z")), Is.Empty);
    }

    [Test]
    public void A_window_with_neither_end_is_refused()
    {
        Assert.That(Condition.Validate(new JsonObject()), Is.EqualTo(new[] { "Choose when it starts, when it ends, or both." }));
        Assert.That(Condition.Validate(Window("", "  ")), Is.EqualTo(new[] { "Choose when it starts, when it ends, or both." }));
    }

    [Test]
    public void A_window_that_starts_at_or_after_it_ends_is_refused()
    {
        Assert.That(Condition.Validate(Window("2026-12-31T00:00:00Z", "2026-12-01T00:00:00Z")), Is.EqualTo(new[] { "It must start before it ends." }));
        Assert.That(Condition.Validate(Window("2026-12-01T00:00:00Z", "2026-12-01T00:00:00Z")), Is.EqualTo(new[] { "It must start before it ends." }));
    }

    [Test]
    public void The_ends_are_compared_in_UTC_whatever_offset_they_are_written_with()
    {
        // 10:00 at +05:00 is 05:00 UTC, which is before 08:00 UTC.
        Assert.That(Condition.Validate(Window("2026-12-01T10:00:00+05:00", "2026-12-01T08:00:00Z")), Is.Empty);
    }

    [Test]
    public void A_time_it_cannot_read_is_refused_naming_the_end()
    {
        Assert.That(Condition.Validate(Window("next tuesday", "2026-12-01T00:00:00Z")), Is.EqualTo(new[] { "The start 'next tuesday' isn't a date and time it can read." }));
        Assert.That(Condition.Validate(Window(null, "soon")), Is.EqualTo(new[] { "The end 'soon' isn't a date and time it can read." }));
        Assert.That(Condition.Validate(new JsonObject { ["Start"] = 5 }), Is.EqualTo(new[] { "The start isn't a date and time." }));
    }

    [Test]
    public void A_repeating_window_is_refused_and_is_shown_with_its_summary()
    {
        JsonObject repeating = Window("2026-12-01T09:00:00Z", "2026-12-01T17:00:00Z", Recurrence());

        Assert.That(Condition.Validate(repeating), Is.EqualTo(new[] { "Repeating windows can't be edited yet." }));
        Assert.That(Condition.Describe(repeating), Is.EqualTo("From 1 Dec 2026 09:00 UTC until 1 Dec 2026 17:00 UTC (repeats)"));
    }

    [Test]
    public void A_null_recurrence_is_no_recurrence()
    {
        var parameters = Window("2026-12-01T09:00:00Z", null);
        parameters["Recurrence"] = null;

        Assert.That(Condition.Validate(parameters), Is.Empty);
    }

    [Test]
    public void Described_in_UTC_for_each_shape()
    {
        Assert.That(Condition.Describe(Window("2026-12-01T09:00:00Z", "2026-12-31T23:59:00Z")), Is.EqualTo("From 1 Dec 2026 09:00 UTC until 31 Dec 2026 23:59 UTC"));
        Assert.That(Condition.Describe(Window("2026-12-01T09:00:00+01:00", null)), Is.EqualTo("From 1 Dec 2026 08:00 UTC"));
        Assert.That(Condition.Describe(Window(null, "2026-12-31T23:59:00Z")), Is.EqualTo("Until 31 Dec 2026 23:59 UTC"));
        Assert.That(Condition.Describe(Window("nonsense", null)), Is.EqualTo("A time window that can't be read"));
    }

    [Test]
    public void Times_written_the_RFC_1123_way_still_read_and_validate()
    {
        JsonObject window = Window("Tue, 01 Dec 2026 09:00:00 GMT", "Thu, 31 Dec 2026 23:59:00 GMT");

        Assert.That(Condition.Validate(window), Is.Empty);
        Assert.That(Condition.Describe(window), Is.EqualTo("From 1 Dec 2026 09:00 UTC until 31 Dec 2026 23:59 UTC"));
    }

    [Test]
    public async Task A_flag_with_a_window_is_editable_conditions_but_one_that_repeats_is_not()
    {
        var site = new TestSite(Declared);
        site.Store.With("Sale", new FlagSettings(true, [new FlagCondition("TimeWindow", Window("2026-12-01T09:00:00Z", null))]));
        site.Refresh();
        FeatureFlag plain = (await site.Provider.GetRequiredService<FlagCatalog>().FindAsync("Sale", default))!;
        site.Store.With("Sale", new FlagSettings(true, [new FlagCondition("Microsoft.TimeWindow", Window("2026-12-01T09:00:00Z", "2026-12-02T09:00:00Z", Recurrence()))]));
        site.Refresh();
        FeatureFlag repeating = (await site.Provider.GetRequiredService<FlagCatalog>().FindAsync("Sale", default))!;

        Assert.That(plain.State, Is.EqualTo(FeatureFlagState.Conditions));
        Assert.That(plain.Summary, Is.EqualTo("From 1 Dec 2026 09:00 UTC"));
        Assert.That(plain.Actions.Save.Allowed, Is.True);
        Assert.That(repeating.State, Is.EqualTo(FeatureFlagState.Custom));
        Assert.That(repeating.Summary, Does.EndWith("(repeats)"));
        Assert.That(repeating.Actions.Save, Has.Property(nameof(FeatureFlagAction.Allowed)).False
            .And.Property(nameof(FeatureFlagAction.Reason)).EqualTo("Repeating windows can't be edited yet."));
    }

    [TestCase("2026-12-01T09:00:00Z", "2026-12-31T23:59:00Z")]
    [TestCase("Tue, 01 Dec 2026 09:00:00 GMT", "Thu, 31 Dec 2026 23:59:00 GMT")]
    [TestCase("2026-12-01T10:00:00+01:00", "2027-01-01T00:59:00+01:00")]
    public async Task Microsofts_own_filter_is_on_inside_the_window_and_off_outside_it(string start, string end)
    {
        var site = new TestSite(Declared);
        var clock = new FakeTimeProvider();
        site.Provider.GetServices<IFeatureFilterMetadata>().OfType<TimeWindowFilter>().Single().SystemClock = clock;
        site.Store.With("Sale", new FlagSettings(true, [new FlagCondition("Microsoft.TimeWindow", Window(start, end))]));
        Assert.That(Condition.Validate(Window(start, end)), Is.Empty);

        clock.SetUtcNow(new DateTimeOffset(2026, 11, 30, 12, 0, 0, TimeSpan.Zero));
        bool before = await site.IsOnAsync("Sale");
        clock.SetUtcNow(new DateTimeOffset(2026, 12, 15, 12, 0, 0, TimeSpan.Zero));
        bool inside = await site.IsOnAsync("Sale");
        clock.SetUtcNow(new DateTimeOffset(2027, 1, 2, 12, 0, 0, TimeSpan.Zero));
        bool after = await site.IsOnAsync("Sale");

        Assert.That((before, inside, after), Is.EqualTo((false, true, false)));
    }

    [Test]
    public async Task A_window_with_only_a_start_or_only_an_end_works_in_Microsofts_filter()
    {
        var site = new TestSite(Declared);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 12, 15, 12, 0, 0, TimeSpan.Zero));
        site.Provider.GetServices<IFeatureFilterMetadata>().OfType<TimeWindowFilter>().Single().SystemClock = clock;

        site.Store.With("Sale", new FlagSettings(true, [new FlagCondition("Microsoft.TimeWindow", Window("2026-12-01T00:00:00Z", null))]));
        site.Refresh();
        bool started = await site.IsOnAsync("Sale");
        site.Store.With("Sale", new FlagSettings(true, [new FlagCondition("Microsoft.TimeWindow", Window(null, "2026-12-01T00:00:00Z"))]));
        site.Refresh();
        bool ended = await site.IsOnAsync("Sale");

        Assert.That((started, ended), Is.EqualTo((true, false)));
    }
}
