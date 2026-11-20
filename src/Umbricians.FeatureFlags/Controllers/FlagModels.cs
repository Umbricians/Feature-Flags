using System.ComponentModel.DataAnnotations;

namespace Umbricians.FeatureFlags.Controllers;

/// <summary>A flag in the flag list.</summary>
internal sealed class FlagListItemResponseModel
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>The group editors find it in, or null for none.</summary>
    public string? Group { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary><c>configuration</c>, <c>flagSource</c> or <c>code</c>.</summary>
    public required string DeclaredIn { get; init; }

    public bool DeclaredTwice { get; init; }

    /// <summary>False for an override whose flag nobody declares any more.</summary>
    public bool IsDeclared { get; init; }

    /// <summary><c>off</c>, <c>everyone</c>, <c>audience</c> or <c>custom</c> (something the backoffice can't edit).</summary>
    public required string State { get; init; }

    /// <summary>Whether the flag is switched on; a flag that is off may still keep an audience.</summary>
    public bool Enabled { get; init; }

    /// <summary>How many members and groups its audience has, when it has one, even while off.</summary>
    public AudienceCountsModel? Audience { get; init; }

    public bool HasOverride { get; init; }

    public bool DiffersFromDefault { get; init; }

    public bool Editable { get; init; }

    public string? ReadOnlyReason { get; init; }

    public UpdatedByModel? UpdatedBy { get; init; }

    public DateTime? UpdatedUtc { get; init; }
}

/// <summary>A flag with everything its editor shows.</summary>
internal sealed class FlagResponseModel
{
    public required FlagListItemResponseModel Flag { get; init; }

    /// <summary>Its default: <c>off</c>, <c>everyone</c>, <c>audience</c> or <c>custom</c>.</summary>
    public required string DefaultState { get; init; }

    /// <summary>Who it is on for now, by key and id, when the backoffice can read it.</summary>
    public AudienceModel? AudienceDetail { get; init; }

    /// <summary>The override as stored, in Microsoft's <c>feature_management</c> JSON, when there is one.</summary>
    public string? OverrideDefinition { get; init; }
}

internal sealed class AudienceCountsModel
{
    public int Members { get; init; }

    public int MemberGroups { get; init; }

    public int Groups { get; init; }
}

/// <summary>Who a flag is on for.</summary>
internal sealed class AudienceModel
{
    /// <summary>Member keys.</summary>
    public IReadOnlyList<string> Members { get; init; } = [];

    /// <summary>Member group keys.</summary>
    public IReadOnlyList<string> MemberGroups { get; init; } = [];

    /// <summary>Ids of groups from the site's group providers.</summary>
    public IReadOnlyList<string> Groups { get; init; } = [];
}

internal sealed class UpdatedByModel
{
    public Guid Key { get; init; }

    public string? Name { get; init; }
}

/// <summary>What the flag list's filters and the tree offer.</summary>
internal sealed class FlagSummaryResponseModel
{
    public required IReadOnlyList<FlagGroupCountModel> Groups { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    public int Total { get; init; }

    public int Undeclared { get; init; }

    /// <summary>Whether settings from the backoffice are switched off on this site.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>The site's environment, such as Production, so editors know where they are changing flags.</summary>
    public required string Environment { get; init; }
}

internal sealed class FlagGroupCountModel
{
    /// <summary>The group's name, or null for flags in no group.</summary>
    public string? Name { get; init; }

    public int Count { get; init; }
}

/// <summary>A group from one of the site's group providers.</summary>
internal sealed class ProviderGroupResponseModel
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }
}

/// <summary>An editor's settings for a flag.</summary>
internal sealed class SaveFlagRequestModel
{
    public bool Enabled { get; init; }

    /// <summary>Who the flag is on for, or null for everyone.</summary>
    public AudienceModel? Audience { get; init; }
}

internal sealed class SetFlagEnabledRequestModel
{
    [Required]
    public bool? Enabled { get; init; }
}
