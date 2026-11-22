using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

namespace Umbricians.FeatureFlags.Controllers;

/// <summary>
/// A flag, mirroring <c>FeatureFlag</c> field for field with the words the API uses, plus what only the backoffice
/// shows: whether it is on, how many members and groups its audience has, and who is in it.
/// </summary>
internal sealed class FlagResponseModel
{
    public required string Name { get; init; }

    /// <summary>False for an override whose flag nobody declares any more.</summary>
    public bool IsDeclared { get; init; }

    /// <summary><c>configuration</c>, <c>flagSource</c> or <c>code</c>.</summary>
    public required string DeclaredIn { get; init; }

    public bool DeclaredTwice { get; init; }

    /// <summary>The section of the flag list it is in, or null for none.</summary>
    public string? Section { get; init; }

    public string? Description { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>Whether it is declared public, so the headless endpoint may serve it.</summary>
    public bool IsPublic { get; init; }

    /// <summary><c>off</c>, <c>everyone</c>, <c>audience</c>, <c>conditions</c> or <c>custom</c> (something the backoffice can't edit).</summary>
    public required string State { get; init; }

    /// <summary>Its default's state, in the same words.</summary>
    public required string DefaultState { get; init; }

    /// <summary>What applies now, or null when it uses more than settings can say (state <c>custom</c>).</summary>
    public FlagSettingsModel? Settings { get; init; }

    /// <summary>Its default as settings, or null when it uses more than settings can say.</summary>
    public FlagSettingsModel? DefaultSettings { get; init; }

    /// <summary>Its override: who saved it and when, with the definition as stored; null when it has none.</summary>
    public FlagOverrideModel? Override { get; init; }

    public bool DiffersFromDefault { get; init; }

    /// <summary>What may be done with it, each with the reason when not.</summary>
    public required FlagActionsModel Actions { get; init; }

    /// <summary>What its conditions amount to in one line, or null when it has none to describe.</summary>
    public string? Summary { get; init; }

    /// <summary>Whether the flag is switched on; a flag that is off may still keep conditions.</summary>
    public bool Enabled { get; init; }

    /// <summary>How many members and groups its audience has, when it has one, even while off.</summary>
    public AudienceCountsModel? Audience { get; init; }

    /// <summary>Who it is on for, with what each group is, when its only condition is an audience the backoffice can read.</summary>
    public AudienceDetailModel? AudienceDetail { get; init; }
}

/// <summary>A flag's settings: whether it is on, and the conditions that say who or when it is on for.</summary>
internal sealed class FlagSettingsModel
{
    public bool Enabled { get; init; }

    public IReadOnlyList<FlagConditionModel> Conditions { get; init; } = [];

    /// <summary><c>any</c> (the default) or <c>all</c>: whether one condition is enough, or every one must hold.</summary>
    public string Match { get; init; } = "any";
}

/// <summary>One of Microsoft's filters on a flag.</summary>
internal sealed class FlagConditionModel
{
    /// <summary>The filter's name, such as <c>Microsoft.Targeting</c>.</summary>
    [Required]
    public string Name { get; init; } = "";

    /// <summary>The filter's parameters as JSON; empty when it has none.</summary>
    public JsonObject Parameters { get; init; } = new();
}

/// <summary>A flag's override as stored.</summary>
internal sealed class FlagOverrideModel
{
    /// <summary>The flag in Microsoft's <c>feature_management</c> JSON.</summary>
    public required string Definition { get; init; }

    /// <summary>The key of the backoffice user who saved it, when there was one.</summary>
    public Guid? UpdatedBy { get; init; }

    /// <summary>That user's name, when they still exist.</summary>
    public string? UpdatedByName { get; init; }

    public DateTime UpdatedUtc { get; init; }
}

/// <summary>What an editor may do with a flag.</summary>
internal sealed class FlagActionsModel
{
    public required FlagActionModel Switch { get; init; }

    public required FlagActionModel Save { get; init; }

    public required FlagActionModel Reset { get; init; }

    public required FlagActionModel Remove { get; init; }
}

/// <summary>Whether an editor may do one thing with a flag, and why not when they may not.</summary>
internal sealed class FlagActionModel
{
    public bool Allowed { get; init; }

    public string? Reason { get; init; }
}

internal sealed class AudienceCountsModel
{
    public int Members { get; init; }

    public int MemberGroups { get; init; }

    public int Groups { get; init; }
}

/// <summary>Who a flag is on for, as the editor shows it.</summary>
internal sealed class AudienceDetailModel
{
    /// <summary>Member keys.</summary>
    public IReadOnlyList<string> Members { get; init; } = [];

    public IReadOnlyList<AudienceGroupModel> Groups { get; init; } = [];
}

/// <summary>A group in an audience, and what the site knows it to be.</summary>
internal sealed class AudienceGroupModel
{
    /// <summary>What the flag stores: a member group's key or name, or a group provider's id.</summary>
    public required string Id { get; init; }

    /// <summary><c>memberGroup</c>, <c>namedMemberGroup</c>, <c>providerGroup</c> or <c>unknown</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>What editors see, or null when the site doesn't know the group.</summary>
    public string? Name { get; init; }
}

/// <summary>What the flag list's filters offer, and what the list says about the site.</summary>
internal sealed class FlagSummaryResponseModel
{
    public required IReadOnlyList<FlagSectionCountModel> Sections { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    public int Total { get; init; }

    public int Undeclared { get; init; }

    /// <summary>Whether settings from the backoffice are switched off on this site.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>The site's environment, such as Production, so editors know where they are changing flags.</summary>
    public required string Environment { get; init; }
}

internal sealed class FlagSectionCountModel
{
    /// <summary>The section's name, or null for flags in no section.</summary>
    public string? Name { get; init; }

    public int Count { get; init; }
}

/// <summary>A condition registered on the site: its filter name, which a manifest's <c>meta.name</c> matches, and what editors call it.</summary>
internal sealed class ConditionResponseModel
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }
}

/// <summary>A group from one of the site's group providers.</summary>
internal sealed class ProviderGroupResponseModel
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }
}

/// <summary>The settings an editor or script saves for a flag.</summary>
internal sealed class SaveFlagRequestModel
{
    public bool Enabled { get; init; }

    /// <summary>What must hold for the flag to be on; none means everyone.</summary>
    public IReadOnlyList<FlagConditionModel> Conditions { get; init; } = [];

    /// <summary><c>any</c> (the default) or <c>all</c>.</summary>
    public string Match { get; init; } = "any";
}

internal sealed class SetFlagEnabledRequestModel
{
    [Required]
    public bool? Enabled { get; init; }
}
