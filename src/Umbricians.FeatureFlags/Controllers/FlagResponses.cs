using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;

namespace Umbricians.FeatureFlags.Controllers;

/// <summary>Turns catalog flags into the API's response models.</summary>
internal sealed class FlagResponses
{
    private readonly IUserService _users;
    private readonly AudienceGroups _groups;

    public FlagResponses(IUserService users, AudienceGroups groups)
    {
        _users = users;
        _groups = groups;
    }

    public async Task<IReadOnlyList<FlagListItemResponseModel>> ListItemsAsync(IReadOnlyList<FeatureFlag> flags, CancellationToken cancellationToken) =>
        ListItems(flags, await ReadGroupsAsync(flags, cancellationToken), await UserNamesAsync(flags));

    public async Task<FlagResponseModel> FlagAsync(FeatureFlag flag, CancellationToken cancellationToken)
    {
        GroupDirectory? groups = await ReadGroupsAsync([flag], cancellationToken);
        return new FlagResponseModel
        {
            Flag = ListItems([flag], groups, await UserNamesAsync([flag]))[0],
            DefaultState = ApiNames.State(flag.DefaultState),
            AudienceDetail = flag.Settings?.Audience is { } audience ? Detail(audience, groups!) : null,
            OverrideDefinition = flag.Override?.Definition,
        };
    }

    public static string DeclaredInName(FeatureFlagDeclaredIn declaredIn) => declaredIn switch
    {
        FeatureFlagDeclaredIn.Configuration => "configuration",
        FeatureFlagDeclaredIn.FlagSource => "flagSource",
        _ => "code",
    };

    /// <summary>Reads the site's groups once for the whole response, and only when a flag has an audience to describe.</summary>
    private async Task<GroupDirectory?> ReadGroupsAsync(IReadOnlyList<FeatureFlag> flags, CancellationToken cancellationToken) =>
        flags.Any(flag => flag.Settings?.Audience is not null) ? await _groups.ReadAsync(cancellationToken) : null;

    private async Task<IReadOnlyDictionary<Guid, string?>> UserNamesAsync(IReadOnlyList<FeatureFlag> flags)
    {
        Guid[] userKeys = flags.Select(flag => flag.Override?.UpdatedBy).OfType<Guid>().Distinct().ToArray();
        return userKeys.Length == 0
            ? new Dictionary<Guid, string?>()
            : (await _users.GetAsync(userKeys)).ToDictionary(user => user.Key, user => user.Name);
    }

    private static FlagListItemResponseModel[] ListItems(IReadOnlyList<FeatureFlag> flags, GroupDirectory? groups, IReadOnlyDictionary<Guid, string?> userNames) =>
        flags.Select(flag => ListItem(flag, groups, userNames)).ToArray();

    private static AudienceDetailModel Detail(FlagAudience audience, GroupDirectory groups) => new()
    {
        Members = audience.Members,
        Groups = audience.Groups.Select(groups.Classify).Select(group => new AudienceGroupModel
        {
            Id = group.Id,
            Kind = ApiNames.GroupKind(group.Kind),
            Name = group.Name,
        }).ToArray(),
    };

    /// <summary>How many of each an audience has: member groups include those named in appsettings; unknown ids count with provider groups.</summary>
    private static AudienceCountsModel Counts(FlagAudience audience, GroupDirectory groups)
    {
        AudienceGroupKind[] kinds = audience.Groups.Select(id => groups.Classify(id).Kind).ToArray();
        int memberGroups = kinds.Count(kind => kind is AudienceGroupKind.MemberGroup or AudienceGroupKind.NamedMemberGroup);
        return new AudienceCountsModel { Members = audience.Members.Count, MemberGroups = memberGroups, Groups = kinds.Length - memberGroups };
    }

    private static FlagActionsModel Actions(FeatureFlagActions actions) => new()
    {
        Switch = Action(actions.Switch),
        Save = Action(actions.Save),
        Reset = Action(actions.Reset),
        Remove = Action(actions.Remove),
    };

    private static FlagActionModel Action(FeatureFlagAction action) => new() { Allowed = action.Allowed, Reason = action.Reason };

    private static FlagListItemResponseModel ListItem(FeatureFlag flag, GroupDirectory? groups, IReadOnlyDictionary<Guid, string?> userNames)
    {
        Guid? updatedBy = flag.Override?.UpdatedBy;
        return new FlagListItemResponseModel
        {
            Name = flag.Name,
            Description = flag.Description,
            Section = flag.Section,
            Tags = flag.Tags,
            DeclaredIn = DeclaredInName(flag.DeclaredIn),
            DeclaredTwice = flag.DeclaredTwice,
            IsDeclared = flag.IsDeclared,
            State = ApiNames.State(flag.State),
            Enabled = flag.Settings?.Enabled ?? flag.State != FeatureFlagState.Off,
            Audience = flag.Settings?.Audience is { } audience ? Counts(audience, groups!) : null,
            HasOverride = flag.Override is not null,
            DiffersFromDefault = flag.DiffersFromDefault,
            Actions = Actions(flag.Actions),
            UpdatedBy = updatedBy is null ? null : new UpdatedByModel { Key = updatedBy.Value, Name = userNames.GetValueOrDefault(updatedBy.Value) },
            UpdatedUtc = flag.Override?.UpdatedUtc,
        };
    }
}
