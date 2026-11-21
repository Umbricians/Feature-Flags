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
    private readonly FlagResolution _resolution;
    private readonly CodeFlags _code;
    private readonly ConfigurationFlagDetails _configurationDetails;
    private readonly IOptionsMonitor<FeatureFlagsOptions> _options;

    public FlagCatalog(
        FlagResolution resolution,
        CodeFlags code,
        ConfigurationFlagDetails configurationDetails,
        IOptionsMonitor<FeatureFlagsOptions> options)
    {
        _resolution = resolution;
        _code = code;
        _configurationDetails = configurationDetails;
        _options = options;
    }

    public async Task<IReadOnlyList<CatalogFlag>> GetAllAsync(CancellationToken cancellationToken)
    {
        ResolvedFlags resolved = await _resolution.GetAllAsync(cancellationToken);
        bool readOnly = _options.CurrentValue.OverridesAreReadOnly();

        return resolved.Flags.Select(flag => flag switch
        {
            { Declared: null } => Undeclared(flag.Override!, readOnly),
            { Declared.ReadError: { } error } => Unreadable(flag.Declared, FlagDetailsFor(flag.Declared), flag.Override, error, readOnly),
            _ => Describe(flag, FlagDetailsFor(flag.Declared), readOnly),
        }).ToArray();
    }

    // Finds the flag in the whole list, not by asking the flag source for one name: the list is what the editor sees,
    // so a change is checked against exactly that.
    public async Task<CatalogFlag?> FindAsync(string name, CancellationToken cancellationToken) =>
        (await GetAllAsync(cancellationToken)).FirstOrDefault(flag => string.Equals(flag.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>What helps editors find the flag: configuration's details first, then code's.</summary>
    private FlagDetails FlagDetailsFor(DeclaredFlag declared)
    {
        FlagDetails fromCode = _code.Find(declared.Name)?.Details ?? FlagDetails.None;
        return declared.DeclaredIn == FlagDeclaredIn.Code
            ? fromCode
            : _configurationDetails.Find(declared.Name).Or(fromCode);
    }

    private const string ReadOnlySiteReason = "Settings from the backoffice are switched off on this site (Umbricians:FeatureFlags:Overrides is ReadOnly).";
    private const string UndeclaredReason = "No flag source declares this flag any more, so its setting has no effect.";
    private const string NoOverrideReason = "It has no setting saved here, so it already uses its default.";
    private const string DeclaredReason = "A declared flag can't be removed; reset it to go back to its default.";
    private const string UnreadableSettingsReason = "Its settings can't be read, so the backoffice can't change them.";

    /// <summary>What an editor may do with a flag. The one place that decides: the service enforces it, the API reports it.</summary>
    private static FlagActions ActionsFor(bool readOnlySite, bool isDeclared, FlagSettings? settings, string? notEditableReason, bool hasOverride)
    {
        if (readOnlySite)
        {
            FlagAction refused = FlagAction.Refuse(ReadOnlySiteReason);
            return new FlagActions(refused, refused, refused, refused);
        }

        if (!isDeclared)
        {
            FlagAction undeclared = FlagAction.Refuse(UndeclaredReason);
            return new FlagActions(undeclared, undeclared, undeclared, FlagAction.Allow);
        }

        FlagAction edit = settings is not null && notEditableReason is null
            ? FlagAction.Allow
            : FlagAction.Refuse(notEditableReason ?? UnreadableSettingsReason);
        return new FlagActions(
            edit,
            edit,
            hasOverride ? FlagAction.Allow : FlagAction.Refuse(NoOverrideReason),
            FlagAction.Refuse(DeclaredReason));
    }

    private static CatalogFlag Describe(ResolvedFlag flag, FlagDetails details, bool readOnly)
    {
        DeclaredFlag declared = flag.Declared!;
        FeatureDefinition defaultDefinition = declared.Default!;
        SavedOverride? entry = flag.Override;
        FlagSettings? defaultSettings = FlagDefinitions.ToSettings(defaultDefinition, out string? defaultReason);

        FlagSettings? overrideSettings = entry?.Settings;
        string? overrideReason = entry?.NotEditableReason;

        FlagSettings? settings = flag.OverrideApplies ? overrideSettings : defaultSettings;
        FlagDisplayState state = flag.OverrideApplies
            ? Display(overrideSettings, flag.Definition!)
            : Display(defaultSettings, defaultDefinition);

        string? notEditableReason = entry is not null ? overrideReason : defaultReason;

        bool differs = entry is not null
            && (overrideSettings is null || defaultSettings is null || !overrideSettings.SameAs(defaultSettings));

        return new CatalogFlag(
            declared.Name,
            IsDeclared: true,
            declared.DeclaredIn,
            declared.DeclaredTwice,
            details.ForFlag(declared.Name),
            defaultSettings,
            Display(defaultSettings, defaultDefinition),
            entry?.Override,
            settings,
            state,
            ActionsFor(readOnly, isDeclared: true, settings, notEditableReason, hasOverride: entry is not null),
            differs);
    }

    private static CatalogFlag Unreadable(DeclaredFlag declared, FlagDetails details, SavedOverride? entry, Exception exception, bool readOnly)
    {
        string reason = $"Its definition can't be read, so checking it fails: {(exception.InnerException ?? exception).Message}";
        return new CatalogFlag(
            declared.Name,
            IsDeclared: true,
            declared.DeclaredIn,
            declared.DeclaredTwice,
            details.ForFlag(declared.Name),
            DefaultSettings: null,
            DefaultState: FlagDisplayState.Custom,
            entry?.Override,
            Settings: null,
            State: FlagDisplayState.Custom,
            ActionsFor(readOnly, isDeclared: true, settings: null, reason, hasOverride: entry is not null),
            DiffersFromDefault: entry is not null);
    }

    private static CatalogFlag Undeclared(SavedOverride entry, bool readOnly)
    {
        FlagSettings? settings = entry.Settings;
        FlagDisplayState state = settings is null ? FlagDisplayState.Custom : (FlagDisplayState)settings.State;
        return new CatalogFlag(
            entry.Override.FlagName,
            IsDeclared: false,
            FlagDeclaredIn.Code,
            DeclaredTwice: false,
            FlagDetails.None.ForFlag(entry.Override.FlagName),
            DefaultSettings: null,
            DefaultState: FlagDisplayState.Off,
            entry.Override,
            settings,
            state,
            ActionsFor(readOnly, isDeclared: false, settings, notEditableReason: null, hasOverride: true),
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
/// <param name="Actions">What an editor may do with it, each with the reason when not.</param>
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
    FlagActions Actions,
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
