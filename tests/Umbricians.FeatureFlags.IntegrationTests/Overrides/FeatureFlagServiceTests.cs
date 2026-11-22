using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Umbraco.Cms.Core.Scoping;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.IntegrationTests.Overrides;

/// <summary>An editor's change, saved in SQL Server, seen by the Microsoft feature managers in a scope of its own.</summary>
public class FeatureFlagServiceTests : IntegrationTest
{
    private FeatureFlagService Service => Site.Services.GetRequiredService<FeatureFlagService>();

    [Test]
    public async Task A_flag_declared_off_is_off_until_an_editor_switches_it_on_and_off_again_after_a_reset()
    {
        Assert.That(await IsEnabledAsync("Tests.Off"), Is.False);

        Assert.That((await Service.SaveAsync("Tests.Off", FlagSettings.ForAudience(true, null), null, default)).Status, Is.EqualTo(FeatureFlagResultStatus.Saved));
        Assert.That(await IsEnabledAsync("Tests.Off"), Is.True);
        Assert.That(await IsEnabledAsync("Tests.Off", variants: true), Is.True);

        Assert.That((await Service.ResetAsync("Tests.Off", default)).Status, Is.EqualTo(FeatureFlagResultStatus.Saved));
        Assert.That(await IsEnabledAsync("Tests.Off"), Is.False);
        Assert.That(await IsEnabledAsync("Tests.Off", variants: true), Is.False);
    }

    [Test]
    public async Task A_flag_declared_on_can_be_switched_off_and_a_reset_turns_it_back_on()
    {
        await Service.SwitchAsync("Tests.On", false, null, default);
        Assert.That(await IsEnabledAsync("Tests.On"), Is.False);

        await Service.ResetAsync("Tests.On", default);

        Assert.That(await IsEnabledAsync("Tests.On"), Is.True);
    }

    [Test]
    public async Task A_flag_declared_in_code_can_be_switched_on_and_reset()
    {
        await Service.SwitchAsync("Tests.InCode", true, null, default);
        Assert.That(await IsEnabledAsync("Tests.InCode"), Is.True);

        await Service.ResetAsync("Tests.InCode", default);

        Assert.That(await IsEnabledAsync("Tests.InCode"), Is.False);
    }

    [Test]
    public async Task A_save_is_in_the_database_with_who_made_it_and_a_reset_removes_it()
    {
        var editor = Guid.NewGuid();
        await Service.SwitchAsync("Tests.Off", true, editor, default);

        FeatureFlagOverride saved = (await Site.Services.GetRequiredService<IFeatureFlagOverrideStore>().GetAllAsync(default)).Single();
        Assert.That(saved, Has.Property(nameof(FeatureFlagOverride.FlagName)).EqualTo("Tests.Off")
            .And.Property(nameof(FeatureFlagOverride.UpdatedBy)).EqualTo(editor));

        await Service.ResetAsync("Tests.Off", default);

        Assert.That(await Site.Services.GetRequiredService<IFeatureFlagOverrideStore>().GetAllAsync(default), Is.Empty);
    }

    [Test]
    public async Task A_flag_nobody_declared_cannot_be_saved()
    {
        Assert.That((await Service.SaveAsync("Tests.Nobody", FlagSettings.ForAudience(true, null), null, default)).Status, Is.EqualTo(FeatureFlagResultStatus.NotFound));
    }

    [Test]
    public async Task The_public_service_saves_checks_and_resets_a_flag_on_SQL_Server()
    {
        IFeatureFlagService service = Site.Services.GetRequiredService<IFeatureFlagService>();
        var editor = Guid.NewGuid();

        FeatureFlagResult saved = await service.SaveAsync("tests.off", FlagSettings.ForAudience(true, null), editor);

        Assert.That(saved.Status, Is.EqualTo(FeatureFlagResultStatus.Saved));
        Assert.That(await IsEnabledAsync("Tests.Off"), Is.True);
        FeatureFlag flag = (await service.GetAsync("Tests.Off"))!;
        Assert.That(flag, Has.Property(nameof(FeatureFlag.State)).EqualTo(FeatureFlagState.Everyone)
            .And.Property(nameof(FeatureFlag.DiffersFromDefault)).True);
        Assert.That(flag.Override!.UpdatedBy, Is.EqualTo(editor));

        Assert.That((await service.SwitchAsync("Tests.Off", false)).IsSaved, Is.True);
        Assert.That(await IsEnabledAsync("Tests.Off"), Is.False);

        Assert.That((await service.ResetAsync("Tests.Off")).IsSaved, Is.True);
        Assert.That((await service.GetAsync("Tests.Off"))!.Override, Is.Null);
        Assert.That(await IsEnabledAsync("Tests.Off"), Is.False);
        Assert.That((await service.SaveAsync("Tests.Nobody", FlagSettings.Off)).Status, Is.EqualTo(FeatureFlagResultStatus.NotFound));
    }

    [Test]
    public async Task A_handler_in_the_test_site_cancels_a_save_and_a_reset_of_a_flag_and_the_messages_come_back()
    {
        IFeatureFlagService service = Site.Services.GetRequiredService<IFeatureFlagService>();

        FeatureFlagResult save = await service.SaveAsync("Frozen.Checkout", FlagSettings.ForAudience(true, null));
        FeatureFlagResult reset = await service.ResetAsync("Frozen.Checkout");

        Assert.That(save.Status, Is.EqualTo(FeatureFlagResultStatus.Cancelled));
        Assert.That(save.Messages, Is.EqualTo(new[] { "Frozen.Checkout is frozen." }));
        Assert.That(reset.Status, Is.Not.EqualTo(FeatureFlagResultStatus.Saved));
        Assert.That(await Site.Services.GetRequiredService<IFeatureFlagOverrideStore>().GetAllAsync(default), Is.Empty);
        Assert.That(await IsEnabledAsync("Frozen.Checkout"), Is.False);
        Assert.That((await service.SaveAsync("Tests.Off", FlagSettings.ForAudience(true, null))).IsSaved, Is.True);
        await service.ResetAsync("Tests.Off");
    }

    [Test]
    public async Task A_save_inside_an_open_scope_does_not_hang_and_is_seen_once_the_scope_completes()
    {
        IFeatureFlagService service = Site.Services.GetRequiredService<IFeatureFlagService>();
        ICoreScopeProvider scopes = Site.Services.GetRequiredService<ICoreScopeProvider>();

        using (ICoreScope scope = scopes.CreateCoreScope())
        {
            FeatureFlagResult saved = await service.SaveAsync("Tests.Off", FlagSettings.ForAudience(true, null)).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.That(saved.IsSaved, Is.True);
            scope.Complete();
        }

        Assert.That(await IsEnabledAsync("Tests.Off"), Is.True);

        using (ICoreScope scope = scopes.CreateCoreScope())
        {
            await service.ResetAsync("Tests.Off").WaitAsync(TimeSpan.FromSeconds(30));
            scope.Complete();
        }

        Assert.That(await IsEnabledAsync("Tests.Off"), Is.False);
        Assert.That(await Site.Services.GetRequiredService<IFeatureFlagOverrideStore>().GetAllAsync(default), Is.Empty);
    }

    [Test]
    public async Task A_save_inside_a_scope_that_is_not_completed_is_rolled_back_and_never_seen()
    {
        IFeatureFlagService service = Site.Services.GetRequiredService<IFeatureFlagService>();
        ICoreScopeProvider scopes = Site.Services.GetRequiredService<ICoreScopeProvider>();

        using (scopes.CreateCoreScope())
        {
            await service.SaveAsync("Tests.Off", FlagSettings.ForAudience(true, null)).WaitAsync(TimeSpan.FromSeconds(30));
        }

        Assert.That(await IsEnabledAsync("Tests.Off"), Is.False);
        Assert.That(await Site.Services.GetRequiredService<IFeatureFlagOverrideStore>().GetAllAsync(default), Is.Empty);
    }

    private async Task<bool> IsEnabledAsync(string flagName, bool variants = false)
    {
        await using AsyncServiceScope scope = Site.CreateScope();
        return variants
            ? await scope.ServiceProvider.GetRequiredService<IVariantFeatureManager>().IsEnabledAsync(flagName)
            : await scope.ServiceProvider.GetRequiredService<IFeatureManager>().IsEnabledAsync(flagName);
    }
}
