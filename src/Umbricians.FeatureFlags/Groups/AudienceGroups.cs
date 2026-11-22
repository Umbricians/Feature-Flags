using Microsoft.Extensions.Logging;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Targeting;

namespace Umbricians.FeatureFlags.Groups;

/// <summary>What a group id in an audience is.</summary>
internal enum AudienceGroupKind
{
    /// <summary>A member group, by key.</summary>
    MemberGroup,

    /// <summary>A member group, by name, as written in appsettings.</summary>
    NamedMemberGroup,

    /// <summary>A group a group provider offers.</summary>
    ProviderGroup,

    /// <summary>Nothing the site knows: the key of a deleted member group, or a provider's group that is no longer offered.</summary>
    Unknown,
}

/// <summary>A group id from an audience, with what it is and, when the site knows it, its name.</summary>
internal sealed record AudienceGroup(string Id, AudienceGroupKind Kind, string? Name);

/// <summary>
/// Reads, once per request, what every group is: the site's member groups and the groups every group provider offers.
/// Never cached, because member groups change while the site runs.
/// </summary>
internal sealed class AudienceGroups(IMemberGroups memberGroups, IEnumerable<IFeatureFlagGroupProvider> providers, ILogger<AudienceGroups> logger)
{
    public async Task<GroupDirectory> ReadAsync(CancellationToken cancellationToken)
    {
        var offered = new List<FeatureFlagGroup>();
        foreach (IFeatureFlagGroupProvider provider in providers)
        {
            try
            {
                offered.AddRange(await provider.GetGroupsAsync(cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception,
                    "The Feature Flags group provider {Provider} failed to list its groups, so the backoffice offers none of them for this request.",
                    provider.GetType().FullName);
            }
        }

        return new GroupDirectory(offered, await memberGroups.GetAllAsync(cancellationToken));
    }

    /// <summary>
    /// Asks every group provider for its groups, for the health check and startup log, and says which ones failed.
    /// A provider that takes longer than <paramref name="timeout"/> counts as failed, so a hanging provider can't hang
    /// the check. Providers are asked afresh rather than remembered, so the answer is true now.
    /// </summary>
    public async Task<IReadOnlyList<FailingProvider>> FindFailingProvidersAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        // Together, and each provider's call on the thread pool, so one that blocks synchronously only costs its own timeout.
        FailingProvider?[] results = await Task.WhenAll(providers.Select(async provider =>
        {
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                limit.CancelAfter(timeout);
                await Task.Run(() => provider.GetGroupsAsync(limit.Token), CancellationToken.None).WaitAsync(timeout, cancellationToken);
                return null;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested || exception is not OperationCanceledException)
            {
                return new FailingProvider(provider.GetType().FullName ?? provider.GetType().Name, exception);
            }
        }));

        return results.OfType<FailingProvider>().ToArray();
    }
}

/// <summary>A group provider that couldn't list its groups in time, and why.</summary>
internal sealed record FailingProvider(string Name, Exception Exception);

/// <summary>What the site's groups were when <see cref="AudienceGroups.ReadAsync"/> read them.</summary>
internal sealed class GroupDirectory(IReadOnlyList<FeatureFlagGroup> providerGroups, IReadOnlyList<KnownMemberGroup> memberGroups)
{
    public IReadOnlyList<FeatureFlagGroup> ProviderGroups { get; } = providerGroups;

    /// <summary>
    /// Says what a group id is. Provider ids come first, since a provider's id is meant to be unique across member group
    /// keys, so a clash is the site's bug and the provider's group wins in the editor.
    /// </summary>
    public AudienceGroup Classify(string id)
    {
        FeatureFlagGroup? provided = ProviderGroups.FirstOrDefault(group => string.Equals(group.Id, id, StringComparison.Ordinal));
        if (provided is not null)
        {
            return new AudienceGroup(id, AudienceGroupKind.ProviderGroup, provided.Name);
        }

        if (Guid.TryParse(id, out Guid key) && memberGroups.FirstOrDefault(group => group.Key == key) is { } byKey)
        {
            return new AudienceGroup(id, AudienceGroupKind.MemberGroup, byKey.Name);
        }

        // Targeting compares group names as they are, so a name matches only exactly.
        return memberGroups.Any(group => string.Equals(group.Name, id, StringComparison.Ordinal))
            ? new AudienceGroup(id, AudienceGroupKind.NamedMemberGroup, id)
            : new AudienceGroup(id, AudienceGroupKind.Unknown, null);
    }

    /// <summary>
    /// Members and member group keys written the way targeting compares them; every other id trimmed and kept as it is;
    /// blanks and repeats dropped.
    /// </summary>
    public FlagAudience Normalize(FlagAudience audience)
    {
        string WriteMember(string id) => Guid.TryParse(id, out Guid key) ? TargetingIds.For(key) : id;
        string WriteGroup(string id) => Classify(id).Kind == AudienceGroupKind.MemberGroup ? TargetingIds.For(Guid.Parse(id)) : id;

        // A group's percentage follows it to the way its id is written, the first of any repeats winning.
        var percentages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Every percentage is added before the 100s are dropped, so a repeat that is in full can't hide the first one's share.
        foreach (string group in audience.Groups.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            percentages.TryAdd(WriteGroup(group.Trim()), audience.PercentageOf(group));
        }

        foreach (string full in percentages.Where(pair => pair.Value == 100).Select(pair => pair.Key).ToArray())
        {
            percentages.Remove(full);
        }

        return new FlagAudience(Clean(audience.Members, WriteMember), Clean(audience.Groups, WriteGroup))
        {
            DefaultPercentage = audience.DefaultPercentage,
            GroupPercentages = percentages,
            ExcludedMembers = Clean(audience.ExcludedMembers, WriteMember),
            ExcludedGroups = Clean(audience.ExcludedGroups, WriteGroup),
        };
    }

    private static string[] Clean(IEnumerable<string> ids, Func<string, string> write) => ids
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => write(id.Trim()))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
