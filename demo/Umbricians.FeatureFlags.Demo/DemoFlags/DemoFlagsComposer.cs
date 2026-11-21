using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Notifications;
using Umbricians.FeatureFlags.Extensions;

namespace Umbricians.FeatureFlags.Demo.DemoFlags;

/// <summary>
/// Shows off the package: a flag declared in code, a group provider, and test members to sign in as. Demo only.
/// </summary>
public class DemoFlagsComposer : IComposer
{
    public const string DarkMode = "DarkMode";
    public const string MemberDiscount = "Pricing.MemberDiscount";

    /// <summary>The flags declared here in code rather than in appsettings, so the demo page can label them.</summary>
    public static readonly IReadOnlySet<string> DeclaredInCode = new HashSet<string> { DarkMode, MemberDiscount };

    public void Compose(IUmbracoBuilder builder)
    {
        builder.AddFeatureFlags(flags => flags
            .Declare(DarkMode, description: "Dark colours across the site.", tags: ["ui"])
            .Declare(MemberDiscount, description: "10% off the price for the members it is on for.", group: "Pricing", tags: ["pricing"])
            .AddGroupProvider<ExampleDomainGroupProvider>());

        builder.Services.Configure<DemoMembersOptions>(builder.Config.GetSection("FeatureFlagsDemo:Members"));
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, DemoMemberSeeder>();
    }
}

/// <summary>Test members, from <c>FeatureFlagsDemo:Members</c> in the Development settings only.</summary>
public class DemoMembersOptions
{
    public string? Password { get; set; }

    public List<DemoMemberAccount> Accounts { get; set; } = [];
}

public class DemoMemberAccount
{
    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> Groups { get; set; } = [];
}
