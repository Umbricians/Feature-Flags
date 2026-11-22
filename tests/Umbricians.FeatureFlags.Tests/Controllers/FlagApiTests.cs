using System.Text.Json.Nodes;
using Umbraco.Cms.Core.Events;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbricians.FeatureFlags.Configuration;
using Umbricians.FeatureFlags.Controllers;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Groups;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.Tests.Controllers;

/// <summary>What the backoffice API says about flags and their audiences, and what it accepts.</summary>
public class FlagApiTests
{
    private static readonly Guid PremiumKey = Guid.Parse("7f3a9c21-4e8b-4d6f-a1c5-0b2e9d4f8a67");
    private static readonly Guid GoneKey = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid Editor = Guid.Parse("99999999-2222-4333-8444-555555555555");
    private const string Flags = """
        { "feature_management": { "feature_flags": [
            { "id": "Checkout", "enabled": false },
            { "id": "Search", "enabled": true },
            { "id": "Sale", "enabled": true, "conditions": { "client_filters": [ { "name": "Microsoft.Percentage", "parameters": { "Start": "Mon, 01 Jan 2024 00:00:00 GMT" } } ] } } ] } }
        """;

    private sealed class CrmProvider : IFeatureFlagGroupProvider
    {
        public Task<IEnumerable<FeatureFlagGroup>> GetGroupsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<FeatureFlagGroup>>([new FeatureFlagGroup("crm:premium", "Premium in the CRM", "Pays us.")]);

        public Task<IEnumerable<string>> GetGroupIdsForMemberAsync(FeatureFlagMember member, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<string>>([]);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Staging";

        public string ApplicationName { get; set; } = "Test";

        public string ContentRootPath { get; set; } = "/";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static TestSite Site(string appsettings = Flags)
    {
        var site = new TestSite(appsettings, before: s => s.AddSingleton<IFeatureFlagGroupProvider>(new CrmProvider()));
        site.MemberGroups.Add(PremiumKey, "Premium members").Add(Guid.NewGuid(), "VIPs");
        return site;
    }

    private static FlagResponses Responses(TestSite site, string userName = "Ada") => new(
        Stub.Of<IUserService>((method, args) => method == nameof(IUserService.GetAsync)
            ? Task.FromResult<IEnumerable<IUser>>(((IEnumerable<Guid>)args[0]!).Select(key => Stub.Of<IUser>((name, _) => name switch
            {
                "get_Key" => key,
                "get_Name" => userName,
                _ => null,
            })).ToArray())
            : null),
        site.Provider.GetRequiredService<AudienceGroups>());

    private static FlagsController Controller(TestSite site, string userName = "Ada") => new(
        site.Provider.GetRequiredService<FlagList>(),
        site.Provider.GetRequiredService<IFeatureFlagService>(),
        Responses(site, userName),
        site.Provider.GetRequiredService<AudienceGroups>(),
        site.Provider.GetRequiredService<ConditionRegistry>(),
        new TestEnvironment());

    private static ManageFlagsController Manager(TestSite site) => new(
        site.Provider.GetRequiredService<FeatureFlagService>(),
        Responses(site),
        Stub.Of<IBackOfficeSecurityAccessor>((_, _) => null));

    private static FlagAudience AnAudience() => new(
        ["m1", "m2"],
        [PremiumKey.ToString(), "VIPs", "crm:premium", "crm:gone", GoneKey.ToString()]);

    private static async Task<FlagResponseModel> GetFlagAsync(FlagsController controller, string name) =>
        (FlagResponseModel)((OkObjectResult)await controller.Flag(name, default)).Value!;

    [Test]
    public async Task A_flag_says_what_each_of_its_groups_is()
    {
        TestSite site = Site();
        site.Store.With("Checkout", FlagSettings.ForAudience(true, AnAudience()));
        site.Refresh();

        FlagResponseModel flag = await GetFlagAsync(Controller(site), "Checkout");

        Assert.That(flag.AudienceDetail!.Members, Is.EqualTo(new[] { "m1", "m2" }));
        Assert.That(flag.AudienceDetail.Groups.Select(group => (group.Id, group.Kind, group.Name)), Is.EqualTo(new (string, string, string?)[]
        {
            (PremiumKey.ToString(), "memberGroup", "Premium members"),
            ("VIPs", "namedMemberGroup", "VIPs"),
            ("crm:premium", "providerGroup", "Premium in the CRM"),
            ("crm:gone", "unknown", null),
            (GoneKey.ToString(), "unknown", null),
        }));
        Assert.That(flag.Audience, Is.Not.Null.And.Property(nameof(AudienceCountsModel.Members)).EqualTo(2)
            .And.Property(nameof(AudienceCountsModel.MemberGroups)).EqualTo(2)
            .And.Property(nameof(AudienceCountsModel.Groups)).EqualTo(3));
        Assert.That(flag.DefaultState, Is.EqualTo("off"));
        Assert.That(flag.Override!.Definition, Is.Not.Null);
    }

    [Test]
    public async Task A_flag_lists_what_may_be_done_with_it_and_why_not()
    {
        TestSite site = Site();
        site.Store.With("Search", FlagSettings.Off).With("Promo.Removed", FlagSettings.Off);
        site.Refresh();

        FlagActionsModel withOverride = (await GetFlagAsync(Controller(site), "Search")).Actions;
        FlagActionsModel noOverride = (await GetFlagAsync(Controller(site), "Checkout")).Actions;
        FlagActionsModel undeclared = (await GetFlagAsync(Controller(site), "Promo.Removed")).Actions;

        Assert.That((withOverride.Switch.Allowed, withOverride.Reset.Allowed, withOverride.Remove.Allowed), Is.EqualTo((true, true, false)));
        Assert.That(noOverride.Reset, Has.Property(nameof(FlagActionModel.Allowed)).False
            .And.Property(nameof(FlagActionModel.Reason)).Contain("already uses its default"));
        Assert.That((undeclared.Save.Allowed, undeclared.Remove.Allowed, undeclared.Remove.Reason), Is.EqualTo((false, true, (string?)null)));
    }

    [Test]
    public async Task A_flag_with_an_audience_beside_other_conditions_still_says_what_its_groups_are()
    {
        TestSite site = Site();
        FlagCondition audience = FlagSettings.ForAudience(true, AnAudience()).Conditions[0];
        site.Store.With("Checkout", new FlagSettings(true, [audience, new FlagCondition("Contoso.Culture", new JsonObject())], ConditionMatch.All));
        site.Refresh();

        FlagResponseModel flag = await GetFlagAsync(Controller(site), "Checkout");

        Assert.That(flag.AudienceDetail!.Members, Is.EqualTo(new[] { "m1", "m2" }));
        Assert.That(flag.AudienceDetail.Groups.Select(group => group.Kind), Does.Contain("memberGroup"));
    }

    [Test]
    public async Task A_flag_describes_its_percentages_and_who_is_left_out()
    {
        TestSite site = Site();
        site.Store.With("Checkout", FlagSettings.ForAudience(true, new FlagAudience(["m1"], ["crm:premium"])
        {
            DefaultPercentage = 30,
            GroupPercentages = new Dictionary<string, int> { ["crm:premium"] = 50 },
            ExcludedMembers = ["m2"],
            ExcludedGroups = ["VIPs", PremiumKey.ToString()],
        }));
        site.Refresh();

        FlagResponseModel flag = await GetFlagAsync(Controller(site), "Checkout");

        Assert.That(flag.State, Is.EqualTo("audience"));
        Assert.That(flag.AudienceDetail!.DefaultPercentage, Is.EqualTo(30));
        Assert.That(flag.AudienceDetail.Groups.Single().Percentage, Is.EqualTo(50));
        Assert.That(flag.AudienceDetail.ExcludedMembers, Is.EqualTo(new[] { "m2" }));
        Assert.That(flag.AudienceDetail.ExcludedGroups.Select(group => (group.Kind, group.Name)), Is.EqualTo(new (string, string?)[]
        {
            ("namedMemberGroup", "VIPs"),
            ("memberGroup", "Premium members"),
        }));
        Assert.That((flag.Audience!.DefaultPercentage, flag.Audience.ExcludedMembers, flag.Audience.ExcludedGroups), Is.EqualTo((30, 1, 2)));
    }

    [Test]
    public async Task A_flag_without_an_audience_has_no_audience_detail()
    {
        TestSite site = Site();

        FlagResponseModel flag = await GetFlagAsync(Controller(site), "Search");

        Assert.That(flag.AudienceDetail, Is.Null);
        Assert.That(flag.Audience, Is.Null);
        Assert.That(flag.Enabled, Is.True);
    }

    [Test]
    public async Task The_list_counts_audiences_and_names_who_changed_each_flag()
    {
        TestSite site = Site();
        await site.Store.SaveAsync(new FeatureFlagOverride("Checkout", FlagDefinitions.ToJson("Checkout", FlagSettings.ForAudience(true, AnAudience())), Editor, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)), default);
        site.Refresh();

        var page = (PagedViewModel<FlagResponseModel>)((OkObjectResult)await Controller(site).Flags(cancellationToken: default)).Value!;

        Assert.That(page.Total, Is.EqualTo(3));
        FlagResponseModel checkout = page.Items.Single(item => item.Name == "Checkout");
        Assert.That(checkout.Audience, Has.Property(nameof(AudienceCountsModel.MemberGroups)).EqualTo(2));
        Assert.That(checkout.Override, Has.Property(nameof(FlagOverrideModel.UpdatedByName)).EqualTo("Ada").And.Property(nameof(FlagOverrideModel.UpdatedBy)).EqualTo(Editor));
        Assert.That(page.Items.Single(item => item.Name == "Search").Audience, Is.Null);
        Assert.That(page.Items.Single(item => item.Name == "Sale").State, Is.EqualTo("custom"));
        Assert.That(page.Items.Select(item => item.DeclaredIn), Is.All.EqualTo("configuration"));
    }

    [Test]
    public async Task The_list_refuses_a_page_or_filter_it_does_not_know()
    {
        FlagsController controller = Controller(Site());

        Assert.That(await controller.Flags(skip: -1), Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(await controller.Flags(take: 101), Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(await controller.Flags(state: "sideways"), Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task The_list_can_be_filtered_by_section_and_tag()
    {
        FlagsController controller = Controller(Site());

        var none = (PagedViewModel<FlagResponseModel>)((OkObjectResult)await controller.Flags(section: "", tag: "beta", filter: "Che")).Value!;

        Assert.That(none.Total, Is.Zero);
    }

    [Test]
    public async Task The_list_understands_every_filter_the_backoffice_sends()
    {
        TestSite site = Site();
        site.Store.With("Search", FlagSettings.Off);
        site.Refresh();

        var page = (PagedViewModel<FlagResponseModel>)((OkObjectResult)await Controller(site).Flags(
            state: "off", show: "declared", marks: "changed,differs", orderBy: "updated", filter: "Sea")).Value!;

        Assert.That(page.Items.Select(item => item.Name), Is.EqualTo(new[] { "Search" }));
        Assert.That(await Controller(site).Flags(marks: "sparkly"), Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(await Controller(site).Flags(orderBy: "sideways"), Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(await Controller(site).Flags(show: "everything"), Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task The_summary_counts_flags_and_says_where_the_site_is()
    {
        TestSite site = Site();
        site.Store.With("Gone", FlagSettings.Off);
        site.Refresh();

        FlagSummaryResponseModel summary = await Controller(site).Summary(default);

        Assert.That(summary.Total, Is.EqualTo(3));
        Assert.That(summary.Undeclared, Is.EqualTo(1));
        Assert.That(summary.Environment, Is.EqualTo("Staging"));
        Assert.That(summary.ReadOnly, Is.False);
        Assert.That(summary.Sections.Single().Count, Is.EqualTo(3));
    }

    [Test]
    public async Task A_flag_nobody_knows_is_not_found()
    {
        Assert.That(await Controller(Site()).Flag("Nope", default), Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task Offers_the_groups_the_providers_offer()
    {
        IEnumerable<ProviderGroupResponseModel> groups = await Controller(Site()).ProviderGroups(default);

        Assert.That(groups.Single(), Has.Property(nameof(ProviderGroupResponseModel.Id)).EqualTo("crm:premium")
            .And.Property(nameof(ProviderGroupResponseModel.Description)).EqualTo("Pays us."));
    }

    [Test]
    public void Offers_the_registered_conditions_by_name()
    {
        IEnumerable<ConditionResponseModel> conditions = Controller(Site()).Conditions();

        Assert.That(conditions.Select(condition => (condition.Name, condition.DisplayName)), Does.Contain(("Microsoft.Targeting", "Audience")));
    }

    private static SaveFlagRequestModel Request(bool enabled, string match = "any", params FlagConditionModel[] conditions) =>
        new() { Enabled = enabled, Match = match, Conditions = conditions };

    private static FlagConditionModel Audience(string[] members, string[] groups) => new()
    {
        Name = "Microsoft.Targeting",
        Parameters = FlagSettings.ForAudience(true, new FlagAudience(members, groups)).Conditions[0].Parameters,
    };

    [Test]
    public async Task Saving_sends_every_group_id_through_as_the_editor_chose_it_and_returns_the_flag()
    {
        TestSite site = Site();

        IActionResult result = await Manager(site).Save("Checkout", Request(true, "any", Audience(["m1"], [PremiumKey.ToString().ToUpperInvariant(), "VIPs", "crm:premium"])), default);

        var flag = (FlagResponseModel)((OkObjectResult)result).Value!;
        Assert.That(flag, Has.Property(nameof(FlagResponseModel.Name)).EqualTo("Checkout")
            .And.Property(nameof(FlagResponseModel.State)).EqualTo("audience"));
        Assert.That(flag.Settings!.Conditions.Single().Name, Is.EqualTo("Microsoft.Targeting"));
        Assert.That(FlagDefinitions.ReadOverride(site.Store.All.Single().Definition, out _)!.Audience!.Groups,
            Is.EqualTo(new[] { PremiumKey.ToString(), "VIPs", "crm:premium" }));
    }

    [Test]
    public async Task Saving_without_conditions_is_on_for_everyone()
    {
        TestSite site = Site();

        await Manager(site).Save("Checkout", Request(true), default);

        Assert.That(FlagDefinitions.ReadOverride(site.Store.All.Single().Definition, out _)!.State, Is.EqualTo(FlagState.Everyone));
    }

    [Test]
    public async Task Saving_with_a_match_the_api_does_not_know_is_a_bad_request()
    {
        Assert.That(await Manager(Site()).Save("Checkout", Request(true, "most"), default), Is.InstanceOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task Saving_a_flag_nobody_declares_is_not_found_and_a_custom_one_is_a_conflict()
    {
        ManageFlagsController manager = Manager(Site());

        Assert.That(await manager.Save("Nope", Request(false), default), Is.InstanceOf<NotFoundObjectResult>());
        Assert.That(await manager.Save("Sale", Request(false), default), Is.InstanceOf<ConflictObjectResult>());
    }

    [Test]
    public async Task Saving_conditions_nobody_registered_or_that_are_invalid_is_a_conflict_that_lists_the_problems()
    {
        TestSite site = Site();

        var conflict = (ConflictObjectResult)await Manager(site).Save("Checkout", Request(true, "any", new FlagConditionModel { Name = "Nobody.Registered" }), default);

        var problem = (ProblemDetails)conflict.Value!;
        Assert.That(problem.Extensions["problems"], Is.Not.Empty);
        Assert.That(problem.Detail, Is.Not.Empty);
        Assert.That(site.Store.All, Is.Empty);
    }

    [Test]
    public async Task Switching_and_resetting_a_flag_answer_for_it()
    {
        TestSite site = Site();
        ManageFlagsController manager = Manager(site);

        Assert.That(await manager.SetEnabled("Checkout", new SetFlagEnabledRequestModel { Enabled = true }, default), Is.InstanceOf<OkObjectResult>());
        Assert.That(site.Store.All, Has.Count.EqualTo(1));
        Assert.That(await manager.Reset("Checkout", default), Is.InstanceOf<OkObjectResult>());
        Assert.That(site.Store.All, Is.Empty);
    }

    [Test]
    public async Task A_save_a_handler_cancelled_is_a_conflict_that_carries_its_messages()
    {
        TestSite site = Site();
        site.Events.On<FeatureFlagOverrideSavingNotification>(saving =>
            saving.CancelOperation(new EventMessage("Frozen", "Checkout is frozen.", EventMessageType.Error)));

        var conflict = (ConflictObjectResult)await Manager(site).Save("Checkout", Request(true), default);

        var problem = (ProblemDetails)conflict.Value!;
        Assert.That(problem.Detail, Is.EqualTo("Checkout is frozen."));
        Assert.That(problem.Extensions["messages"], Is.EqualTo(new[] { "Checkout is frozen." }));
        Assert.That(site.Store.All, Is.Empty);
    }

    [Test]
    public async Task A_cancelled_change_with_no_message_still_says_why_it_was_refused()
    {
        TestSite site = Site();
        site.Events.On<FeatureFlagOverrideSavingNotification>(saving => saving.Cancel = true);

        var conflict = (ConflictObjectResult)await Manager(site).SetEnabled("Checkout", new SetFlagEnabledRequestModel { Enabled = true }, default);

        Assert.That(((ProblemDetails)conflict.Value!).Detail, Does.Contain("cancelled"));
    }
}
