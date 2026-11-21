using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Configuration;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// What each flag is, where it is declared, and what applies now. The one place that knows the flag source comes before
/// code, and that an override applies unless overrides are read-only or its JSON can't be read.
/// </summary>
/// <remarks>
/// Microsoft asks for a definition on every check and keeps no copy, so this answers from the
/// <see cref="OverrideCache"/> and the flag source, never the database.
/// </remarks>
internal sealed class FlagResolution
{
    private readonly FlagSource _source;
    private readonly CodeFlags _code;
    private readonly ConfigurationFlagDetails _configuration;
    private readonly OverrideCache _overrides;
    private readonly IOptionsMonitor<FeatureFlagsOptions> _options;
    private readonly ILogger<FlagResolution> _logger;

    public FlagResolution(
        FlagSource source,
        CodeFlags code,
        ConfigurationFlagDetails configuration,
        OverrideCache overrides,
        IOptionsMonitor<FeatureFlagsOptions> options,
        ILogger<FlagResolution> logger)
    {
        _source = source;
        _code = code;
        _configuration = configuration;
        _overrides = overrides;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Hot path (every flag check): the declared flag with what applies now, or null when neither the flag source nor
    /// code declares it. Exceptions from the flag source propagate, as Microsoft expects.
    /// </summary>
    public async Task<ResolvedFlag?> FindAsync(string name)
    {
        FeatureDefinition? definition = await _source.Inner.GetFeatureDefinitionAsync(name);
        DeclaredFlag declared;
        if (definition is not null)
        {
            declared = new DeclaredFlag(definition.Name, definition, _source.DeclaredIn, _code.Find(name) is not null, null);
        }
        else if (_code.Find(name) is { } declaration)
        {
            declared = new DeclaredFlag(declaration.Definition.Name, declaration.Definition, FlagDeclaredIn.Code, false, null);
        }
        else
        {
            return null;
        }

        // Read-only overrides never apply, so a check doesn't even look at them.
        OverrideCache.Entry? entry = null;
        if (!_options.CurrentValue.OverridesAreReadOnly())
        {
            OverrideCache.Snapshot snapshot = await _overrides.GetAsync();
            snapshot.Overrides.TryGetValue(declared.Name, out entry);
        }

        return Resolve(declared, entry);
    }

    /// <summary>Every declared flag once, flag source first and then code, without overrides. Never throws for one bad flag.</summary>
    public Task<DeclaredFlags> GetDeclaredAsync(CancellationToken cancellationToken) => ListDeclaredAsync([], cancellationToken);

    /// <summary>Every declared flag with what applies now, then each override no flag source declares (undeclared flags).</summary>
    public async Task<ResolvedFlags> GetAllAsync(CancellationToken cancellationToken)
    {
        OverrideCache.Snapshot snapshot = await _overrides.GetAsync(cancellationToken);
        DeclaredFlags declared = await ListDeclaredAsync(snapshot.Overrides.Keys, cancellationToken);

        var resolved = new List<ResolvedFlag>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DeclaredFlag flag in declared.Flags)
        {
            seen.Add(flag.Name);
            snapshot.Overrides.TryGetValue(flag.Name, out OverrideCache.Entry? entry);
            resolved.Add(Resolve(flag, entry));
        }

        foreach (OverrideCache.Entry entry in snapshot.Overrides.Values)
        {
            if (!seen.Contains(entry.Override.FlagName))
            {
                resolved.Add(new ResolvedFlag(entry.Override.FlagName, null, entry, false, null));
            }
        }

        return new ResolvedFlags(resolved, declared.ListingError);
    }

    private ResolvedFlag Resolve(DeclaredFlag declared, OverrideCache.Entry? entry)
    {
        if (declared.Default is null)
        {
            return new ResolvedFlag(declared.Name, declared, entry, false, null);
        }

        bool applies = !_options.CurrentValue.OverridesAreReadOnly() && entry?.Definition is not null;
        return new ResolvedFlag(
            declared.Name,
            declared,
            entry,
            applies,
            applies ? Named(entry!.Definition!, declared.Name) : declared.Default);
    }

    /// <summary>
    /// The override's definition under the name the flag source spells. The cached definition is shared, so when its
    /// spelling differs a copy is used rather than renaming it.
    /// </summary>
    private static FeatureDefinition Named(FeatureDefinition definition, string name) =>
        string.Equals(definition.Name, name, StringComparison.Ordinal)
            ? definition
            : new FeatureDefinition
            {
                Name = name,
                EnabledFor = definition.EnabledFor,
                RequirementType = definition.RequirementType,
                Status = definition.Status,
                Allocation = definition.Allocation,
                Variants = definition.Variants,
                Telemetry = definition.Telemetry,
            };

    private async Task<DeclaredFlags> ListDeclaredAsync(IEnumerable<string> alsoTry, CancellationToken cancellationToken)
    {
        var flags = new List<DeclaredFlag>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Exception? listingError = null;

        try
        {
            await foreach (FeatureDefinition definition in _source.Inner.GetAllFeatureDefinitionsAsync().WithCancellation(cancellationToken))
            {
                if (seen.Add(definition.Name))
                {
                    flags.Add(new DeclaredFlag(definition.Name, definition, _source.DeclaredIn, _code.Find(definition.Name) is not null, null));
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One flag the flag source can't read can stop the whole list, so read the flags it is likely to declare one
            // by one, and keep each that fails as unreadable rather than losing the list.
            listingError = exception;
            _logger.LogWarning(exception, "Feature Flags couldn't list every flag the site declares, so it is reading them one by one.");

            IEnumerable<string> names = (_source.DeclaredIn == FlagDeclaredIn.Configuration ? _configuration.DeclaredNames() : [])
                .Concat(_code.All.Select(declaration => declaration.Definition.Name))
                .Concat(alsoTry);
            foreach (string name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seen.Contains(name))
                {
                    continue;
                }

                bool alsoInCode = _code.Find(name) is not null;
                try
                {
                    // Null means the flag source doesn't declare it; code may, which the next step adds.
                    if (await _source.Inner.GetFeatureDefinitionAsync(name) is { } definition && seen.Add(definition.Name))
                    {
                        flags.Add(new DeclaredFlag(definition.Name, definition, _source.DeclaredIn, alsoInCode, null));
                    }
                }
                catch (Exception flagException) when (flagException is not OperationCanceledException)
                {
                    seen.Add(name);
                    flags.Add(new DeclaredFlag(name, null, _source.DeclaredIn, alsoInCode, flagException));
                }
            }
        }

        foreach (CodeFlagDeclaration declaration in _code.All)
        {
            if (seen.Add(declaration.Definition.Name))
            {
                flags.Add(new DeclaredFlag(declaration.Definition.Name, declaration.Definition, FlagDeclaredIn.Code, false, null));
            }
        }

        return new DeclaredFlags(flags, listingError);
    }
}

/// <summary>A declared flag as its flag source or code declares it. <paramref name="Default"/> is null exactly when <paramref name="ReadError"/> is set.</summary>
internal sealed record DeclaredFlag(string Name, FeatureDefinition? Default, FlagDeclaredIn DeclaredIn, bool DeclaredTwice, Exception? ReadError);

/// <summary>Every declared flag, and why the flag source couldn't list them all, when it couldn't.</summary>
internal sealed record DeclaredFlags(IReadOnlyList<DeclaredFlag> Flags, Exception? ListingError);

/// <summary>A flag with what applies now. <paramref name="Definition"/> is what Microsoft evaluates, null when the flag is undeclared or unreadable.</summary>
internal sealed record ResolvedFlag(string Name, DeclaredFlag? Declared, OverrideCache.Entry? Override, bool OverrideApplies, FeatureDefinition? Definition)
{
    public bool IsDeclared => Declared is not null;
}

/// <summary>Every flag with what applies now, and why the flag source couldn't list them all, when it couldn't.</summary>
internal sealed record ResolvedFlags(IReadOnlyList<ResolvedFlag> Flags, Exception? ListingError);
