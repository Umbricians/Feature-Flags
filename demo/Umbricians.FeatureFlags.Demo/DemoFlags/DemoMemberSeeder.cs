using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace Umbricians.FeatureFlags.Demo.DemoFlags;

/// <summary>
/// Creates the test members and their member groups on startup if they don't exist yet. Demo only.
/// </summary>
public class DemoMemberSeeder : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string MemberTypeAlias = "Member";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRuntimeState _runtimeState;
    private readonly IOptions<DemoMembersOptions> _options;
    private readonly ILogger<DemoMemberSeeder> _logger;

    public DemoMemberSeeder(
        IServiceScopeFactory scopeFactory,
        IRuntimeState runtimeState,
        IOptions<DemoMembersOptions> options,
        ILogger<DemoMemberSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _runtimeState = runtimeState;
        _options = options;
        _logger = logger;
    }

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        DemoMembersOptions options = _options.Value;
        if (_runtimeState.Level != RuntimeLevel.Run || string.IsNullOrEmpty(options.Password))
        {
            return;
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        IMemberManager memberManager = scope.ServiceProvider.GetRequiredService<IMemberManager>();
        IMemberGroupService memberGroups = scope.ServiceProvider.GetRequiredService<IMemberGroupService>();

        foreach (string groupName in options.Accounts.SelectMany(account => account.Groups).Distinct())
        {
            if (await memberGroups.GetByNameAsync(groupName) is null)
            {
                await memberGroups.CreateAsync(new MemberGroup { Name = groupName });
            }
        }

        foreach (DemoMemberAccount account in options.Accounts)
        {
            MemberIdentityUser? member = await memberManager.FindByEmailAsync(account.Email);
            if (member is null)
            {
                member = MemberIdentityUser.CreateNew(account.Email, account.Email, MemberTypeAlias, isApproved: true, account.Name);
                var created = await memberManager.CreateAsync(member, options.Password);
                if (!created.Succeeded)
                {
                    _logger.LogWarning("Could not create demo member {Email}: {Errors}", account.Email,
                        string.Join("; ", created.Errors.Select(e => e.Description)));
                    continue;
                }

                member = await memberManager.FindByEmailAsync(account.Email);
            }

            IList<string> roles = await memberManager.GetRolesAsync(member!);
            string[] missing = account.Groups.Except(roles, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missing.Length > 0)
            {
                await memberManager.AddToRolesAsync(member!, missing);
            }
        }
    }
}
