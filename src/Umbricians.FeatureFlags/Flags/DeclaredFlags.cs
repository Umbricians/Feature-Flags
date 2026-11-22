using Microsoft.Extensions.Configuration;
using Microsoft.FeatureManagement;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>A flag declared in code with <c>AddFeatureFlags(flags => flags.Declare(…))</c>.</summary>
internal sealed record CodeFlagDeclaration(FeatureDefinition Definition, FlagDetails Details);

/// <summary>What helps editors find and understand a flag. Microsoft has no place for these. A detail that is null was not given, so another source may give it; an explicit <c>false</c> for <c>IsPublic</c> beats a later source's <c>true</c>.</summary>
internal sealed record FlagDetails(string? Description, string? Section, IReadOnlyList<string> Tags, bool? IsPublic = null)
{
    public static FlagDetails None { get; } = new(null, null, []);

    /// <summary>Each detail from this, or from <paramref name="fallback"/> where this has none.</summary>
    public FlagDetails Or(FlagDetails fallback) =>
        new(Description ?? fallback.Description, Section ?? fallback.Section, Tags.Count > 0 ? Tags : fallback.Tags, IsPublic ?? fallback.IsPublic);

    /// <summary>These details, with the section taken from the flag's name when none was given.</summary>
    public FlagDetails ForFlag(string name) => Section is null ? this with { Section = SectionFromName(name) } : this;

    /// <summary>The text before the first <c>.</c> in a flag's name, when there is any.</summary>
    private static string? SectionFromName(string name)
    {
        int dot = name.IndexOf('.');
        return dot > 0 ? name[..dot] : null;
    }
}

/// <summary>Where a declared flag comes from.</summary>
public enum FeatureFlagDeclaredIn
{
    /// <summary>appsettings, environment variables or any other .NET configuration source.</summary>
    Configuration,

    /// <summary>A flag source the site registered itself, such as its own <see cref="IFeatureDefinitionProvider"/>.</summary>
    FlagSource,

    /// <summary>The site's code, through <c>AddFeatureFlags</c>.</summary>
    Code,
}

/// <summary>The site's own flag source, which Feature Flags wraps.</summary>
internal sealed class FlagSource(IFeatureDefinitionProvider inner)
{
    public IFeatureDefinitionProvider Inner { get; } = inner;

    public FeatureFlagDeclaredIn DeclaredIn { get; } =
        inner is ConfigurationFeatureDefinitionProvider ? FeatureFlagDeclaredIn.Configuration : FeatureFlagDeclaredIn.FlagSource;
}

/// <summary>Flags marked public with <c>MakePublic</c>, whichever flag source declares them.</summary>
internal sealed record PublicFlagNames(IReadOnlyList<string> Names);

/// <summary>The names of flags marked public with <c>MakePublic</c>, compared the way Microsoft compares flag names.</summary>
internal sealed class PublicFlags(IEnumerable<PublicFlagNames> marked)
{
    private readonly HashSet<string> _names = new(marked.SelectMany(m => m.Names), StringComparer.OrdinalIgnoreCase);

    public bool Contains(string name) => _names.Contains(name);
}

/// <summary>The flags declared in code, by name.</summary>
internal sealed class CodeFlags
{
    private readonly Dictionary<string, CodeFlagDeclaration> _flags = new(StringComparer.OrdinalIgnoreCase);

    public CodeFlags(IEnumerable<CodeFlagDeclaration> declarations)
    {
        // A flag declared twice in code: the last declaration wins, as a later registration does elsewhere in .NET.
        foreach (CodeFlagDeclaration declaration in declarations)
        {
            _flags[declaration.Definition.Name] = declaration;
        }
    }

    public IReadOnlyCollection<CodeFlagDeclaration> All => _flags.Values;

    public CodeFlagDeclaration? Find(string name) => _flags.GetValueOrDefault(name);
}

/// <summary>
/// Reads a flag's description, section, tags and whether it is public from configuration, from fields Microsoft ignores: in the
/// <c>feature_management:feature_flags</c> list, or in a flag's section under <c>FeatureManagement</c>.
/// </summary>
internal sealed class ConfigurationFlagDetails(IConfiguration configuration)
{
    /// <summary>The names of the flags configuration declares, in either of Microsoft's schemas.</summary>
    public IEnumerable<string> DeclaredNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (IConfigurationSection flag in configuration.GetSection("feature_management:feature_flags").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(flag["id"]) && names.Add(flag["id"]!))
            {
                yield return flag["id"]!;
            }
        }

        foreach (IConfigurationSection flag in configuration.GetSection("FeatureManagement").GetChildren())
        {
            if (names.Add(flag.Key))
            {
                yield return flag.Key;
            }
        }
    }

    public FlagDetails Find(string name)
    {
        foreach (IConfigurationSection flag in configuration.GetSection("feature_management:feature_flags").GetChildren())
        {
            if (string.Equals(flag["id"], name, StringComparison.OrdinalIgnoreCase))
            {
                return Read(flag, "description", "section", "tags", "public");
            }
        }

        IConfigurationSection section = configuration.GetSection("FeatureManagement").GetSection(name);
        return section.Exists() ? Read(section, "Description", "Section", "Tags", "Public") : FlagDetails.None;
    }

    private static FlagDetails Read(IConfigurationSection flag, string description, string section, string tags, string isPublic) =>
        new(
            NullIfBlank(flag[description]),
            NullIfBlank(flag[section]),
            flag.GetSection(tags).GetChildren().Select(tag => tag.Value).OfType<string>().Where(tag => tag.Length > 0).ToArray(),
            bool.TryParse(flag[isPublic], out bool value) ? value : null);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
