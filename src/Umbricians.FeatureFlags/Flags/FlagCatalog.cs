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
    private readonly PublicFlags _public;
    private readonly IOptionsMonitor<FeatureFlagsOptions> _options;
    private readonly ConditionRegistry _conditions;

    public FlagCatalog(
        FlagResolution resolution,
        CodeFlags code,
        ConfigurationFlagDetails configurationDetails,
        PublicFlags publicFlags,
        IOptionsMonitor<FeatureFlagsOptions> options,
        ConditionRegistry conditions)
    {
        _conditions = conditions;
        _resolution = resolution;
        _code = code;
        _configurationDetails = configurationDetails;
        _public = publicFlags;
        _options = options;
    }

    public async Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken) =>
        Describe(await _resolution.GetAllAsync(cancellationToken));

    /// <summary>The flags as the backoffice shows them, from a listing a caller already has.</summary>
    public IReadOnlyList<FeatureFlag> Describe(ResolvedFlags resolved)
    {
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
    public async Task<FeatureFlag?> FindAsync(string name, CancellationToken cancellationToken) =>
        (await GetAllAsync(cancellationToken)).FirstOrDefault(flag => string.Equals(flag.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>What helps editors find the flag: configuration's details first, then code's.</summary>
    private FlagDetails FlagDetailsFor(DeclaredFlag declared)
    {
        FlagDetails fromCode = _code.Find(declared.Name)?.Details ?? FlagDetails.None;
        FlagDetails details = declared.DeclaredIn == FeatureFlagDeclaredIn.Code
            ? fromCode
            : _configurationDetails.Find(declared.Name).Or(fromCode);
        return _public.Contains(declared.Name) ? details with { IsPublic = true } : details;
    }

    private const string ReadOnlySiteReason = "Settings from the backoffice are switched off on this site (Umbricians:FeatureFlags:Overrides is ReadOnly).";
    private const string UndeclaredReason = "No flag source declares this flag any more, so its setting has no effect.";
    private const string NoOverrideReason = "It has no setting saved here, so it already uses its default.";
    private const string DeclaredReason = "A declared flag can't be removed; reset it to go back to its default.";
    private const string UnreadableSettingsReason = "Its settings can't be read, so the backoffice can't change them.";

    /// <summary>What an editor may do with a flag. The one place that decides: the service enforces it, the API reports it.</summary>
    private static FeatureFlagActions ActionsFor(bool readOnlySite, bool isDeclared, FlagSettings? settings, string? notEditableReason, bool hasOverride)
    {
        if (readOnlySite)
        {
            FeatureFlagAction refused = FeatureFlagAction.Refuse(ReadOnlySiteReason);
            return new FeatureFlagActions(refused, refused, refused, refused);
        }

        if (!isDeclared)
        {
            FeatureFlagAction undeclared = FeatureFlagAction.Refuse(UndeclaredReason);
            return new FeatureFlagActions(undeclared, undeclared, undeclared, FeatureFlagAction.Allow);
        }

        FeatureFlagAction edit = settings is not null && notEditableReason is null
            ? FeatureFlagAction.Allow
            : FeatureFlagAction.Refuse(notEditableReason ?? UnreadableSettingsReason);
        return new FeatureFlagActions(
            edit,
            edit,
            hasOverride ? FeatureFlagAction.Allow : FeatureFlagAction.Refuse(NoOverrideReason),
            FeatureFlagAction.Refuse(DeclaredReason));
    }

    private FeatureFlag Describe(ResolvedFlag flag, FlagDetails details, bool readOnly)
    {
        DeclaredFlag declared = flag.Declared!;
        FeatureDefinition defaultDefinition = declared.Default!;
        SavedOverride? entry = flag.Override;
        FlagSettings? defaultSettings = FlagDefinitions.ToSettings(defaultDefinition, out string? defaultReason);
        if (declared.DeclaredIn == FeatureFlagDeclaredIn.Configuration && defaultDefinition.Status == FeatureStatus.Disabled
            && _configurationDetails.Raw(declared.Name) is { } raw
            && FlagDefinitions.ReadOverride(raw.ToJsonString(), out string? rawReason) is { } rawSettings)
        {
            // Microsoft's provider drops the filters of a flag configured with "enabled": false, so read them from the
            // configuration itself; otherwise switching it on would save "on for everyone" and lose them.
            defaultSettings = FlagDefinitions.WithoutAlwaysOn(rawSettings);
            defaultReason = rawReason;
        }

        FlagSettings? overrideSettings = entry?.Settings;
        string? overrideReason = entry?.NotEditableReason;

        FlagSettings? settings = flag.OverrideApplies ? overrideSettings : defaultSettings;
        FeatureFlagState state = flag.OverrideApplies
            ? Display(overrideSettings, flag.Definition!)
            : Display(defaultSettings, defaultDefinition);

        string? notEditableReason = (entry is not null ? overrideReason : defaultReason)
            ?? (settings is null ? null : _conditions.NotEditableReason(settings));

        bool differs = entry is not null
            && (overrideSettings is null || defaultSettings is null || !overrideSettings.SameAs(defaultSettings));

        return Build(
            declared.Name,
            isDeclared: true,
            declared.DeclaredIn,
            declared.DeclaredTwice,
            details.ForFlag(declared.Name),
            state,
            Display(defaultSettings, defaultDefinition),
            settings,
            defaultSettings,
            entry?.Override,
            differs,
            ActionsFor(readOnly, isDeclared: true, settings, notEditableReason, hasOverride: entry is not null));
    }

    private FeatureFlag Unreadable(DeclaredFlag declared, FlagDetails details, SavedOverride? entry, Exception exception, bool readOnly)
    {
        string reason = $"Its definition can't be read, so checking it fails: {(exception.InnerException ?? exception).Message}";
        return Build(
            declared.Name,
            isDeclared: true,
            declared.DeclaredIn,
            declared.DeclaredTwice,
            details.ForFlag(declared.Name),
            FeatureFlagState.Custom,
            FeatureFlagState.Custom,
            settings: null,
            defaultSettings: null,
            entry?.Override,
            differsFromDefault: entry is not null,
            ActionsFor(readOnly, isDeclared: true, settings: null, reason, hasOverride: entry is not null));
    }

    private FeatureFlag Undeclared(SavedOverride entry, bool readOnly)
    {
        FlagSettings? settings = entry.Settings;
        FeatureFlagState state = settings is null ? FeatureFlagState.Custom : Display(settings);
        return Build(
            entry.Override.FlagName,
            isDeclared: false,
            FeatureFlagDeclaredIn.Code,
            declaredTwice: false,
            FlagDetails.None.ForFlag(entry.Override.FlagName),
            state,
            FeatureFlagState.Off,
            settings,
            defaultSettings: null,
            entry.Override,
            differsFromDefault: false,
            ActionsFor(readOnly, isDeclared: false, settings, notEditableReason: null, hasOverride: true));
    }

    private FeatureFlag Build(
        string name,
        bool isDeclared,
        FeatureFlagDeclaredIn declaredIn,
        bool declaredTwice,
        FlagDetails details,
        FeatureFlagState state,
        FeatureFlagState defaultState,
        FlagSettings? settings,
        FlagSettings? defaultSettings,
        FeatureFlagOverride? featureFlagOverride,
        bool differsFromDefault,
        FeatureFlagActions actions) =>
        new(
            name,
            isDeclared,
            declaredIn,
            declaredTwice,
            details.Section,
            details.Description,
            details.Tags,
            details.IsPublic ?? false,
            state,
            defaultState,
            settings?.Copy(),
            defaultSettings?.Copy(),
            featureFlagOverride,
            differsFromDefault,
            actions,
            settings is null ? null : _conditions.Summarize(settings));

    // A flag with conditions nobody registered, or that the backoffice can't edit (a targeting condition with percentages
    // or exclusions), shows as custom: save and switch are refused for it, so it can't be called editable conditions.
    private FeatureFlagState Display(FlagSettings settings) =>
        settings.State switch
        {
            FlagState.Off => FeatureFlagState.Off,
            FlagState.Everyone => FeatureFlagState.Everyone,
            FlagState.Audience => FeatureFlagState.Audience,
            _ => _conditions.AllRegistered(settings) && _conditions.NotEditableReason(settings) is null
                ? FeatureFlagState.Conditions
                : FeatureFlagState.Custom,
        };

    private FeatureFlagState Display(FlagSettings? settings, FeatureDefinition definition)
    {
        if (settings is not null)
        {
            return Display(settings);
        }

        return definition.Status == FeatureStatus.Disabled || definition.EnabledFor?.Any() != true
            ? FeatureFlagState.Off
            : FeatureFlagState.Custom;
    }
}
