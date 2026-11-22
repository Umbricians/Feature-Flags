using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Configuration;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Diagnostics;

/// <summary>How serious a diagnostic result is.</summary>
internal enum DiagnosticSeverity
{
    /// <summary>Something is broken: flags don't do what editors expect.</summary>
    Error,

    /// <summary>Something is wrong with some flags or sources, and the rest keep working.</summary>
    Warning,

    /// <summary>Worth knowing, nothing to fix.</summary>
    Info,

    /// <summary>Nothing is wrong: there are no errors or warnings. Info results may still come with it.</summary>
    Success,
}

/// <summary>
/// One thing the diagnostics found. <paramref name="Flags"/> names the flags it is about, for errors and warnings;
/// info results leave it empty and only count in their message, because the health check is open to users who may not
/// have View feature flags.
/// </summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Message">What is wrong and what to do, in plain English, naming the flags for errors and warnings.</param>
/// <param name="Flags">The flags it names; empty for info and success.</param>
/// <param name="Anchor">The heading in the getting started docs that explains it.</param>
/// <param name="Exception">What went wrong underneath, for the log only: never shown in the health check.</param>
internal sealed record DiagnosticResult(
    DiagnosticSeverity Severity,
    string Message,
    IReadOnlyList<string> Flags,
    string Anchor,
    Exception? Exception = null);

/// <summary>
/// Works out every check of the health check, once, for both the startup log and the health check, so they always agree.
/// A check that can't run is reported as a warning rather than thrown, so one failing part never hides the others.
/// </summary>
internal sealed class FlagDiagnostics(
    IServiceProvider services,
    FlagResolution resolution,
    FlagCatalog catalog,
    OverrideCache overrides,
    AudienceGroups groups,
    DeletedRootFlags deletedRoots,
    IOptionsMonitor<FeatureFlagsOptions> options)
{
    /// <summary>How long a group provider has to list its groups before it counts as failed.</summary>
    internal static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(5);

    public const string SettingsUnreadable = "settings-cant-be-read";
    public const string RegisteredTwice = "feature-management-registered-twice";
    public const string FiltersShareAName = "filters-share-a-name";
    public const string FlagSourceLate = "flag-source-registered-late";
    public const string SettingUnreadable = "a-saved-setting-cant-be-read";
    public const string DefinitionUnreadable = "a-definition-cant-be-read";
    public const string ListingFailed = "the-flag-source-cant-list-its-flags";
    public const string GroupProviderFailed = "a-group-provider-fails";
    public const string DeclaredTwice = "a-flag-is-declared-twice";
    public const string DeletedRoot = "a-root-condition-names-a-deleted-root";
    public const string HeadlessNothingPublic = "headless-is-on-but-no-flag-is-public";
    public const string Undeclared = "undeclared-flags";
    public const string ReadOnly = "overrides-are-read-only";
    public const string Custom = "flags-with-a-custom-state";
    public const string AllClear = "everything-is-fine";
    public const string CheckFailed = "the-health-check-cant-run-a-check";

    /// <summary>
    /// Every result, in the order of the health check's table, and a success last when there is no error or warning.
    /// </summary>
    public async Task<IReadOnlyList<DiagnosticResult>> RunAsync(CancellationToken cancellationToken)
    {
        var results = new List<DiagnosticResult>();

        OverrideCache.Snapshot? snapshot = await Guard(results, "the saved settings", cancellationToken, async () =>
        {
            OverrideCache.Snapshot read = await overrides.GetAsync(cancellationToken);

            // While a retry runs in the background the cache keeps its old error; read afresh, so a problem that has been fixed isn't reported.
            return read.ReadError is null ? read : await ReloadedAsync(read);
        });
        if (snapshot?.ReadError is { } readError)
        {
            results.Add(Error(SettingsUnreadable,
                "The settings saved in the backoffice can't be read, so every flag uses its default, or the settings read last. "
                + "The store is failing, or the package's migration hasn't run yet.", readError));
        }

        FilterProblems filters = await Guard(results, "the feature filters", cancellationToken, () => Task.FromResult(CheckFilters()))
            ?? new FilterProblems([], []);
        if (filters.Duplicated.Count > 0)
        {
            results.Add(Error(RegisteredTwice,
                "Microsoft.FeatureManagement is registered more than once, so these filters are too: "
                + $"{string.Join(", ", filters.Duplicated)}. Checking a flag that uses one throws. Feature Flags adds "
                + "Microsoft.FeatureManagement itself: remove the site's AddFeatureManagement() call, or move it before Umbraco is "
                + "added so Feature Flags uses it."));
        }

        if (filters.SharedNames.Count > 0)
        {
            results.Add(Error(FiltersShareAName,
                $"More than one feature filter has the same name, so checking a flag that uses it throws: {string.Join("; ", filters.SharedNames)}. "
                + "Feature Flags registers its own targeting filters in place of Microsoft's, so a site's "
                + "AddFeatureManagement().WithTargeting<T>() must come before Umbraco is added (then Feature Flags replaces "
                + "Microsoft's filter), not after."));
        }

        bool? flagSourceWrapped = await Guard(results, "the flag source", cancellationToken,
            () => Task.FromResult<bool?>(services.GetService<IFeatureDefinitionProvider>() is FeatureFlagsDefinitionProvider));
        if (flagSourceWrapped == false)
        {
            results.Add(Error(FlagSourceLate,
                "The site registered its own flag source after Feature Flags, so settings from the backoffice don't apply. "
                + "Register it before Umbraco is added, and Feature Flags wraps it."));
        }

        if (snapshot is { Unreadable.Count: > 0 })
        {
            results.Add(Warning(SettingUnreadable,
                "A setting saved in the backoffice can't be read, so the flag uses its default: "
                + $"{string.Join(", ", snapshot.Unreadable)}. Open the flag and save its settings again, or reset it.",
                snapshot.Unreadable));
        }

        ResolvedFlags? resolved = await Guard(results, "the declared flags", cancellationToken, () => resolution.GetAllAsync(cancellationToken));
        DeclaredFlag[]? declared = resolved?.Flags.Select(flag => flag.Declared).OfType<DeclaredFlag>().ToArray();
        if (declared is not null)
        {
            string[] unreadable = declared.Where(flag => flag.ReadError is not null).Select(flag => flag.Name).ToArray();
            if (unreadable.Length > 0)
            {
                results.Add(Warning(DefinitionUnreadable,
                    $"A declared flag's definition can't be read, so checking it fails: {string.Join(", ", unreadable)}. "
                    + "Fix it where it is declared.",
                    unreadable,
                    declared.First(flag => flag.ReadError is not null).ReadError));
            }

            if (resolved!.ListingError is not null)
            {
                results.Add(Warning(ListingFailed,
                    "The site's flag source can't list its flags, so the backoffice shows only the flags it could read. "
                    + "Checking a flag by name still works.",
                    [],
                    resolved.ListingError));
            }
        }

        IReadOnlyList<FailingProvider> failingProviders = (await Guard(results, "the group providers", cancellationToken, () => groups.FindFailingProvidersAsync(ProviderTimeout, cancellationToken))) ?? [];
        if (failingProviders.Count > 0)
        {
            results.Add(Warning(GroupProviderFailed,
                $"A group provider failed to list its groups, so the backoffice offers none of its groups: {string.Join(", ", failingProviders.Select(provider => provider.Name))}.",
                [],
                failingProviders.Count == 1 ? failingProviders[0].Exception : new AggregateException(failingProviders.Select(provider => provider.Exception))));
        }

        if (declared is not null)
        {
            string[] twice = declared.Where(flag => flag.DeclaredTwice).Select(flag => flag.Name).ToArray();
            if (twice.Length > 0)
            {
                results.Add(Warning(DeclaredTwice,
                    $"A flag is declared both in code and by the site's flag source, and the flag source's declaration is used: {string.Join(", ", twice)}.",
                    twice));
            }
        }

        // The flags are listed once, from the listing above, for the Root check and for the counts below.
        IReadOnlyList<FeatureFlag>? flags = resolved is null ? null : await Guard(results, "the flags", cancellationToken, () => Task.FromResult(catalog.Describe(resolved)));
        IReadOnlyList<string>? deleted = flags is null ? null : await Guard(results, "the Root conditions", cancellationToken, () => Task.FromResult(deletedRoots.Find(flags)));
        if (deleted is { Count: > 0 })
        {
            results.Add(Warning(DeletedRoot,
                $"A Root condition names a root that has been deleted, so its condition can't match: {string.Join(", ", deleted)}. "
                + "Choose another root, or remove the condition.",
                deleted));
        }

        if (options.CurrentValue.Headless.Enabled && flags is not null && !flags.Any(flag => flag is { IsDeclared: true, IsPublic: true }))
        {
            results.Add(Warning(HeadlessNothingPublic,
                "The headless endpoint is switched on (Headless:Enabled) but no flag is public, so it always answers with an empty list. "
                + "Mark flags public with public: true in Declare(...) or appsettings, or with MakePublic(...).",
                []));
        }

        int undeclared = flags?.Count(flag => !flag.IsDeclared) ?? 0;
        if (undeclared > 0)
        {
            results.Add(Info(Undeclared, $"{Count(undeclared)} undeclared and waiting to be removed."));
        }

        if (options.CurrentValue.OverridesAreReadOnly())
        {
            results.Add(Info(ReadOnly, "Settings from the backoffice are switched off on this site (Overrides is ReadOnly)."));
        }

        int custom = flags?.Count(flag => flag.State == FeatureFlagState.Custom) ?? 0;
        if (custom > 0)
        {
            results.Add(Info(Custom, $"{Count(custom)} in a custom state, which the backoffice can't edit."));
        }

        if (!results.Any(result => result.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning))
        {
            results.Add(new DiagnosticResult(DiagnosticSeverity.Success, "Feature Flags is set up correctly.", [], AllClear));
        }

        return results;
    }

    private async Task<OverrideCache.Snapshot> ReloadedAsync(OverrideCache.Snapshot stale)
    {
        await overrides.ReloadAsync();
        return await overrides.GetAsync();
    }

    private static string Count(int count) => count == 1 ? "1 flag is" : $"{count} flags are";

    private static DiagnosticResult Error(string anchor, string message, Exception? exception = null) =>
        new(DiagnosticSeverity.Error, message, [], anchor, exception);

    private static DiagnosticResult Warning(string anchor, string message, IReadOnlyList<string> flags, Exception? exception = null) =>
        new(DiagnosticSeverity.Warning, message, flags, anchor, exception);

    private static DiagnosticResult Info(string anchor, string message) => new(DiagnosticSeverity.Info, message, [], anchor);

    /// <summary>Runs one part of the check. When it throws, says so as a warning, so the rest still runs.</summary>
    private static async Task<T?> Guard<T>(List<DiagnosticResult> results, string what, CancellationToken cancellationToken, Func<Task<T>> check)
    {
        try
        {
            return await check();
        }
        // A cancellation that isn't the caller's, such as an HTTP client's timeout inside a flag source, is a failed check.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested || exception is not OperationCanceledException)
        {
            results.Add(Warning(CheckFailed, $"Feature Flags couldn't check {what}.", [], exception));
            return default;
        }
    }

    private FilterProblems CheckFilters()
    {
        // In a scope of its own: with scoped feature management the filters are scoped, which the root provider can't give.
        using IServiceScope scope = services.CreateScope();
        IFeatureFilterMetadata[] filters = scope.ServiceProvider.GetServices<IFeatureFilterMetadata>().ToArray();

        string[] duplicated = filters
            .GroupBy(filter => filter.GetType())
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.Name)
            .ToArray();

        // Two filters of the same kind under one name make checking that filter's flags throw. It happens when a site adds
        // Microsoft's targeting (AddFeatureManagement().WithTargeting<T>()) after Feature Flags, which has its own.
        string[] sharedNames = filters
            .GroupBy(filter => (Alias: FilterName(filter.GetType()).ToLowerInvariant(), Contextual: ContextOf(filter.GetType())))
            .Where(group => group.Select(filter => filter.GetType()).Distinct().Count() > 1)
            .Select(group => $"'{FilterName(group.First().GetType())}' ({string.Join(", ", group.Select(filter => filter.GetType().Name))})")
            .ToArray();

        return new FilterProblems(duplicated, sharedNames);
    }

    private sealed record FilterProblems(IReadOnlyList<string> Duplicated, IReadOnlyList<string> SharedNames);

    // The name a flag uses for a filter, as Microsoft works it out: its alias, or its class name without "Filter".
    private static string FilterName(Type filter)
    {
        string? alias = filter.GetCustomAttributes(typeof(FilterAliasAttribute), true).OfType<FilterAliasAttribute>().FirstOrDefault()?.Alias;
        if (alias is not null)
        {
            return alias;
        }

        return filter.Name.EndsWith("Filter", StringComparison.Ordinal) ? filter.Name[..^"Filter".Length] : filter.Name;
    }

    // Filters that take the caller's own context and filters that don't are looked up separately, so only a pair of the same kind clashes.
    private static Type? ContextOf(Type filter) =>
        filter.GetInterfaces().FirstOrDefault(face => face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IContextualFeatureFilter<>))?.GetGenericArguments()[0];
}
