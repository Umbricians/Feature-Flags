using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Configuration;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// Every flag as the backoffice shows it: each declared flag with its default and any override, and each override for
/// a flag nobody declares any more.
/// </summary>
internal sealed class FlagCatalog
{
    private readonly FlagSource _source;
    private readonly CodeFlags _code;
    private readonly ConfigurationFlagDetails _configurationDetails;
    private readonly OverrideCache _overrides;
    private readonly IOptionsMonitor<FeatureFlagsOptions> _options;
    private readonly ILogger<FlagCatalog> _logger;

    public FlagCatalog(
        FlagSource source,
        CodeFlags code,
        ConfigurationFlagDetails configurationDetails,
        OverrideCache overrides,
        IOptionsMonitor<FeatureFlagsOptions> options,
        ILogger<FlagCatalog> logger)
    {
        _source = source;
        _code = code;
        _configurationDetails = configurationDetails;
        _overrides = overrides;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CatalogFlag>> GetAllAsync(CancellationToken cancellationToken)
    {
        OverrideCache.Snapshot snapshot = await _overrides.GetAsync(cancellationToken);
        bool readOnly = _options.CurrentValue.Overrides != FeatureFlagsOverrides.Enabled;

        var flags = new Dictionary<string, CatalogFlag>(StringComparer.OrdinalIgnoreCase);
        void AddDeclared(FeatureDefinition definition)
        {
            if (flags.ContainsKey(definition.Name))
            {
                return;
            }

            CodeFlagDeclaration? alsoInCode = _code.Find(definition.Name);
            FlagDetails details = _configurationDetails.Find(definition.Name).Or(alsoInCode?.Details ?? FlagDetails.None);
            flags[definition.Name] = Describe(definition, _source.DeclaredIn, alsoInCode is not null, details, snapshot, readOnly);
        }

        try
        {
            await foreach (FeatureDefinition definition in _source.Inner.GetAllFeatureDefinitionsAsync().WithCancellation(cancellationToken))
            {
                AddDeclared(definition);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One flag Microsoft can't read stops the whole list, so read the flags configuration declares one by one,
            // and show each that fails as unreadable rather than losing the list.
            _logger.LogWarning(exception, "Feature Flags couldn't list every flag the site declares, so it is reading them one by one.");
            foreach (string name in _configurationDetails.DeclaredNames())
            {
                try
                {
                    if (await _source.Inner.GetFeatureDefinitionAsync(name) is { } definition)
                    {
                        AddDeclared(definition);
                    }
                }
                catch (Exception flagException) when (flagException is not OperationCanceledException)
                {
                    flags.TryAdd(name, Unreadable(name, _code.Find(name) is not null, _configurationDetails.Find(name), snapshot, flagException));
                }
            }
        }

        foreach (CodeFlagDeclaration declaration in _code.All)
        {
            if (!flags.ContainsKey(declaration.Definition.Name))
            {
                flags[declaration.Definition.Name] = Describe(
                    declaration.Definition, FlagDeclaredIn.Code, false, declaration.Details, snapshot, readOnly);
            }
        }

        foreach (OverrideCache.Entry entry in snapshot.Overrides.Values)
        {
            if (!flags.ContainsKey(entry.Override.FlagName))
            {
                flags[entry.Override.FlagName] = Undeclared(entry);
            }
        }

        return flags.Values.ToArray();
    }

    public async Task<CatalogFlag?> FindAsync(string name, CancellationToken cancellationToken) =>
        (await GetAllAsync(cancellationToken)).FirstOrDefault(flag => string.Equals(flag.Name, name, StringComparison.OrdinalIgnoreCase));

    private static CatalogFlag Describe(
        FeatureDefinition definition,
        FlagDeclaredIn declaredIn,
        bool declaredTwice,
        FlagDetails details,
        OverrideCache.Snapshot snapshot,
        bool readOnly)
    {
        FlagSettings? defaultSettings = FlagDefinitions.ToSettings(definition, out string? defaultReason);
        snapshot.Overrides.TryGetValue(definition.Name, out OverrideCache.Entry? entry);

        FlagSettings? overrideSettings = null;
        string? overrideReason = null;
        if (entry is not null)
        {
            overrideSettings = FlagDefinitions.ReadOverride(entry.Override.Definition, out overrideReason);
        }

        bool overrideApplies = entry is not null && !readOnly && entry.Definition is not null;
        FlagSettings? settings = overrideApplies ? overrideSettings : defaultSettings;
        FlagDisplayState state = overrideApplies
            ? Display(overrideSettings, entry!.Definition!)
            : Display(defaultSettings, definition);

        string? readOnlyReason = readOnly
            ? "Settings from the backoffice are switched off on this site (Umbricians:FeatureFlags:Overrides is ReadOnly)."
            : entry is not null ? overrideReason : defaultReason;

        bool differs = entry is not null
            && (overrideSettings is null || defaultSettings is null || !overrideSettings.SameAs(defaultSettings));

        return new CatalogFlag(
            definition.Name,
            IsDeclared: true,
            declaredIn,
            declaredTwice,
            details with { Section = details.Section ?? SectionFromName(definition.Name) },
            defaultSettings,
            Display(defaultSettings, definition),
            entry?.Override,
            settings,
            state,
            Editable: readOnlyReason is null && settings is not null,
            readOnlyReason,
            differs);
    }

    private CatalogFlag Unreadable(string name, bool declaredTwice, FlagDetails details, OverrideCache.Snapshot snapshot, Exception exception)
    {
        snapshot.Overrides.TryGetValue(name, out OverrideCache.Entry? entry);
        string reason = $"Its definition can't be read, so checking it fails: {(exception.InnerException ?? exception).Message}";
        return new CatalogFlag(
            name,
            IsDeclared: true,
            _source.DeclaredIn,
            declaredTwice,
            details with { Section = details.Section ?? SectionFromName(name) },
            DefaultSettings: null,
            DefaultState: FlagDisplayState.Custom,
            entry?.Override,
            Settings: null,
            State: FlagDisplayState.Custom,
            Editable: false,
            reason,
            DiffersFromDefault: entry is not null);
    }

    private static CatalogFlag Undeclared(OverrideCache.Entry entry)
    {
        FlagSettings? settings = FlagDefinitions.ReadOverride(entry.Override.Definition, out _);
        FlagDisplayState state = settings is null ? FlagDisplayState.Custom : (FlagDisplayState)settings.State;
        return new CatalogFlag(
            entry.Override.FlagName,
            IsDeclared: false,
            FlagDeclaredIn.Code,
            DeclaredTwice: false,
            new FlagDetails(null, SectionFromName(entry.Override.FlagName), []),
            DefaultSettings: null,
            DefaultState: FlagDisplayState.Off,
            entry.Override,
            settings,
            state,
            Editable: false,
            "No flag source declares this flag any more, so its setting has no effect.",
            DiffersFromDefault: false);
    }

    private static FlagDisplayState Display(FlagSettings? settings, FeatureDefinition definition)
    {
        if (settings is not null)
        {
            return (FlagDisplayState)settings.State;
        }

        return definition.Status == FeatureStatus.Disabled || definition.EnabledFor?.Any() != true
            ? FlagDisplayState.Off
            : FlagDisplayState.Custom;
    }

    /// <summary>The text before the first <c>.</c> in a flag's name, when there is any.</summary>
    internal static string? SectionFromName(string name)
    {
        int dot = name.IndexOf('.');
        return dot > 0 ? name[..dot] : null;
    }
}

/// <summary>A flag as the backoffice shows it.</summary>
/// <param name="Name">The flag's name, as its flag source spells it.</param>
/// <param name="IsDeclared">False for an override whose flag nobody declares any more.</param>
/// <param name="DeclaredIn">Where the flag is declared; the site's flag source wins when code declares it too.</param>
/// <param name="DeclaredTwice">Whether code and the flag source both declare it.</param>
/// <param name="Details">Its description, section (from its name when none is given) and tags.</param>
/// <param name="DefaultSettings">Its default as the backoffice reads it, or null when it uses more than the backoffice edits.</param>
/// <param name="DefaultState">How its default is evaluated.</param>
/// <param name="Override">An editor's override, when there is one.</param>
/// <param name="Settings">What applies now, when the backoffice can read it.</param>
/// <param name="State">How it is evaluated now.</param>
/// <param name="Editable">Whether the backoffice can change it.</param>
/// <param name="ReadOnlyReason">Why the backoffice can't change it, when it can't.</param>
/// <param name="DiffersFromDefault">Whether its override says something different from its default.</param>
internal sealed record CatalogFlag(
    string Name,
    bool IsDeclared,
    FlagDeclaredIn DeclaredIn,
    bool DeclaredTwice,
    FlagDetails Details,
    FlagSettings? DefaultSettings,
    FlagDisplayState DefaultState,
    FeatureFlagOverride? Override,
    FlagSettings? Settings,
    FlagDisplayState State,
    bool Editable,
    string? ReadOnlyReason,
    bool DiffersFromDefault);

/// <summary>How a flag is evaluated, as the flag list shows it.</summary>
internal enum FlagDisplayState
{
    Off = FlagState.Off,
    Everyone = FlagState.Everyone,
    Audience = FlagState.Audience,

    /// <summary>Something the backoffice can't edit, such as a time window.</summary>
    Custom,
}
