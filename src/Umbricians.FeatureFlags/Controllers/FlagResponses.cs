using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbricians.FeatureFlags.Flags;

namespace Umbricians.FeatureFlags.Controllers;

/// <summary>Turns catalog flags into the API's response models.</summary>
internal sealed class FlagResponses
{
    private readonly IUserService _users;

    public FlagResponses(IUserService users) => _users = users;

    public async Task<IReadOnlyList<FlagListItemResponseModel>> ListItemsAsync(IReadOnlyList<CatalogFlag> flags)
    {
        Guid[] userKeys = flags.Select(flag => flag.Override?.UpdatedBy).OfType<Guid>().Distinct().ToArray();
        Dictionary<Guid, string?> names = userKeys.Length == 0
            ? []
            : (await _users.GetAsync(userKeys)).ToDictionary(user => user.Key, user => user.Name);

        return flags.Select(flag => ListItem(flag, names)).ToArray();
    }

    public async Task<FlagResponseModel> FlagAsync(CatalogFlag flag)
    {
        FlagListItemResponseModel item = (await ListItemsAsync([flag]))[0];
        return new FlagResponseModel
        {
            Flag = item,
            DefaultState = StateName(flag.DefaultState),
            AudienceDetail = flag.Settings?.Audience is { } audience ? Detail(audience) : null,
            OverrideDefinition = flag.Override?.Definition,
        };
    }

    public static string StateName(FlagDisplayState state) => state switch
    {
        FlagDisplayState.Off => "off",
        FlagDisplayState.Everyone => "everyone",
        FlagDisplayState.Audience => "audience",
        _ => "custom",
    };

    public static string DeclaredInName(FlagDeclaredIn declaredIn) => declaredIn switch
    {
        FlagDeclaredIn.Configuration => "configuration",
        FlagDeclaredIn.FlagSource => "flagSource",
        _ => "code",
    };

    /// <summary>
    /// Splits a stored audience's groups into member groups, which are stored by key, and groups from group providers,
    /// whose ids are never keys.
    /// </summary>
    public static AudienceModel Detail(FlagAudience audience) => new()
    {
        Members = audience.Members,
        MemberGroups = audience.Groups.Where(group => Guid.TryParse(group, out _)).ToArray(),
        Groups = audience.Groups.Where(group => !Guid.TryParse(group, out _)).ToArray(),
    };

    private static FlagListItemResponseModel ListItem(CatalogFlag flag, IReadOnlyDictionary<Guid, string?> userNames)
    {
        AudienceModel? audience = flag.Settings?.Audience is { } a ? Detail(a) : null;
        Guid? updatedBy = flag.Override?.UpdatedBy;
        return new FlagListItemResponseModel
        {
            Name = flag.Name,
            Description = flag.Details.Description,
            Group = flag.Details.Group,
            Tags = flag.Details.Tags,
            DeclaredIn = DeclaredInName(flag.DeclaredIn),
            DeclaredTwice = flag.DeclaredTwice,
            IsDeclared = flag.IsDeclared,
            State = StateName(flag.State),
            Enabled = flag.Settings?.Enabled ?? flag.State != FlagDisplayState.Off,
            Audience = audience is null
                ? null
                : new AudienceCountsModel { Members = audience.Members.Count, MemberGroups = audience.MemberGroups.Count, Groups = audience.Groups.Count },
            HasOverride = flag.Override is not null,
            DiffersFromDefault = flag.DiffersFromDefault,
            Editable = flag.Editable,
            ReadOnlyReason = flag.ReadOnlyReason,
            UpdatedBy = updatedBy is null ? null : new UpdatedByModel { Key = updatedBy.Value, Name = userNames.GetValueOrDefault(updatedBy.Value) },
            UpdatedUtc = flag.Override?.UpdatedUtc,
        };
    }
}
