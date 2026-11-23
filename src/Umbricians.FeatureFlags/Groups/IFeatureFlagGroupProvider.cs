namespace Umbricians.FeatureFlags.Groups;

/// <summary>
/// Puts members in groups by a rule of the site's own, such as "joined in the last 30 days" or "premium in the CRM",
/// so flags can be switched on for them. Editors pick these groups in the backoffice beside member groups. Register one
/// with <c>builder.AddFeatureFlags(flags => flags.AddGroupProvider&lt;T&gt;())</c>; a site can register several.
/// </summary>
/// <remarks>
/// Registered as a singleton and asked about the signed-in member on requests that check a flag, so keep
/// <see cref="GetGroupIdsForMemberAsync"/> fast, caching anything slow to look up.
/// </remarks>
public interface IFeatureFlagGroupProvider
{
    /// <summary>
    /// The groups editors can pick. Each id must be unique across every provider and every member group's key, so
    /// prefix them with something of the provider's own, such as <c>crm:premium</c>.
    /// </summary>
    Task<IEnumerable<FeatureFlagGroup>> GetGroupsAsync(CancellationToken cancellationToken);

    /// <summary>The ids of the groups the member is in, from those <see cref="GetGroupsAsync"/> returns.</summary>
    Task<IEnumerable<string>> GetGroupIdsForMemberAsync(FeatureFlagMember member, CancellationToken cancellationToken);
}

/// <summary>A group a <see cref="IFeatureFlagGroupProvider"/> offers.</summary>
/// <param name="Id">What flags store and targeting compares, unique across every provider.</param>
/// <param name="Name">What editors see.</param>
/// <param name="Description">Optional: who is in the group, shown to editors.</param>
public sealed record FeatureFlagGroup(string Id, string Name, string? Description = null);

/// <summary>The signed-in member a flag is being checked for.</summary>
/// <param name="Key">The member's key.</param>
/// <param name="UserName">The member's username, when they have one.</param>
/// <param name="Email">The member's email, when they have one.</param>
/// <param name="MemberGroups">The names of the member groups the member is in.</param>
public sealed record FeatureFlagMember(Guid Key, string? UserName, string? Email, IReadOnlyCollection<string> MemberGroups);
