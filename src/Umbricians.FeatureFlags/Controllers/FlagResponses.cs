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

    public async Task<IReadOnlyList<FlagResponseModel>> ListAsync(IReadOnlyList<FeatureFlag> flags, CancellationToken cancellationToken) =>
        Map(flags, await ReadGroupsAsync(flags, cancellationToken), await UserNamesAsync(flags));

    public async Task<FlagResponseModel> FlagAsync(FeatureFlag flag, CancellationToken cancellationToken) =>
        (await ListAsync([flag], cancellationToken))[0];

    /// <summary>Reads the site's groups once for the whole response, and only when a flag has an audience to describe.</summary>
    private async Task<GroupDirectory?> ReadGroupsAsync(IReadOnlyList<FeatureFlag> flags, CancellationToken cancellationToken) =>
        flags.Any(flag => ReadableAudience(flag) is not null) ? await _groups.ReadAsync(cancellationToken) : null;

    private async Task<IReadOnlyDictionary<Guid, string?>> UserNamesAsync(IReadOnlyList<FeatureFlag> flags)
    {
        Guid[] userKeys = flags.Select(flag => flag.Override?.UpdatedBy).OfType<Guid>().Distinct().ToArray();
        return userKeys.Length == 0
            ? new Dictionary<Guid, string?>()
            : (await _users.GetAsync(userKeys)).ToDictionary(user => user.Key, user => user.Name);
    }

    private static FlagResponseModel[] Map(IReadOnlyList<FeatureFlag> flags, GroupDirectory? groups, IReadOnlyDictionary<Guid, string?> userNames) =>
        flags.Select(flag => Map(flag, groups, userNames)).ToArray();

    /// <summary>The audience of the flag's targeting condition when it is one the backoffice can read, whatever other conditions the flag has.</summary>
    private static FlagAudience? ReadableAudience(FeatureFlag flag) => flag.Settings?.Conditions
        .Where(condition => FlagSettings.IsTargeting(condition.Name))
        .Select(condition => FlagAudience.TryRead(condition.Parameters, out _))
        .FirstOrDefault(audience => audience is not null);

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

    private static FlagSettingsModel? Settings(FlagSettings? settings) => settings is null ? null : new FlagSettingsModel
    {
        Enabled = settings.Enabled,
        Conditions = settings.Conditions.Select(condition => new FlagConditionModel { Name = condition.Name, Parameters = condition.Parameters }).ToArray(),
        Match = ApiNames.Match(settings.Match),
    };

    private static FlagResponseModel Map(FeatureFlag flag, GroupDirectory? groups, IReadOnlyDictionary<Guid, string?> userNames)
    {
        FlagAudience? audience = flag.Settings?.Audience;
        FlagAudience? readable = ReadableAudience(flag);
        return new FlagResponseModel
        {
            Name = flag.Name,
            IsDeclared = flag.IsDeclared,
            DeclaredIn = ApiNames.DeclaredIn(flag.DeclaredIn),
            DeclaredTwice = flag.DeclaredTwice,
            Section = flag.Section,
            Description = flag.Description,
            Tags = flag.Tags,
            IsPublic = flag.IsPublic,
            State = ApiNames.State(flag.State),
            DefaultState = ApiNames.State(flag.DefaultState),
            Settings = Settings(flag.Settings),
            DefaultSettings = Settings(flag.DefaultSettings),
            Override = flag.Override is not { } saved ? null : new FlagOverrideModel
            {
                Definition = saved.Definition,
                UpdatedBy = saved.UpdatedBy,
                UpdatedByName = saved.UpdatedBy is { } key ? userNames.GetValueOrDefault(key) : null,
                UpdatedUtc = saved.UpdatedUtc,
            },
            DiffersFromDefault = flag.DiffersFromDefault,
            Actions = Actions(flag.Actions),
            Summary = flag.Summary,
            Enabled = flag.Settings?.Enabled ?? flag.State != FeatureFlagState.Off,
            Audience = audience is null ? null : Counts(audience, groups!),
            AudienceDetail = readable is null ? null : Detail(readable, groups!),
        };
    }
}
