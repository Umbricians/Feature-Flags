using Microsoft.Extensions.Configuration;
using Microsoft.FeatureManagement;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>A flag declared in code with <c>AddFeatureFlags(flags => flags.Declare(…))</c>.</summary>
internal sealed record CodeFlagDeclaration(FeatureDefinition Definition, FlagDetails Details);

/// <summary>What helps editors find and understand a flag. Microsoft has no place for these.</summary>
internal sealed record FlagDetails(string? Description, string? Group, IReadOnlyList<string> Tags)
{
    public static FlagDetails None { get; } = new(null, null, []);

    /// <summary>Each detail from this, or from <paramref name="fallback"/> where this has none.</summary>
    public FlagDetails Or(FlagDetails fallback) =>
        new(Description ?? fallback.Description, Group ?? fallback.Group, Tags.Count > 0 ? Tags : fallback.Tags);
}

/// <summary>Where a declared flag comes from.</summary>
internal enum FlagDeclaredIn
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

    public FlagDeclaredIn DeclaredIn { get; } =
        inner is ConfigurationFeatureDefinitionProvider ? FlagDeclaredIn.Configuration : FlagDeclaredIn.FlagSource;
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
/// Reads a flag's description, group and tags from configuration, from fields Microsoft ignores: in the
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
                return Read(flag, "description", "group", "tags");
            }
        }

        IConfigurationSection section = configuration.GetSection("FeatureManagement").GetSection(name);
        return section.Exists() ? Read(section, "Description", "Group", "Tags") : FlagDetails.None;
    }

    private static FlagDetails Read(IConfigurationSection flag, string description, string group, string tags) =>
        new(
            NullIfBlank(flag[description]),
            NullIfBlank(flag[group]),
            flag.GetSection(tags).GetChildren().Select(tag => tag.Value).OfType<string>().Where(tag => tag.Length > 0).ToArray());

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
