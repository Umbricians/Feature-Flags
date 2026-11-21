using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Umbricians.FeatureFlags.Flags;
using Umbricians.FeatureFlags.Overrides;

namespace Umbricians.FeatureFlags.IntegrationTests.Overrides;

/// <summary>An editor's change, saved in SQL Server, seen by the Microsoft feature managers in a scope of its own.</summary>
public class OverrideServiceTests : IntegrationTest
{
    private OverrideService Service => Site.Services.GetRequiredService<OverrideService>();

    [Test]
    public async Task A_flag_declared_off_is_off_until_an_editor_switches_it_on_and_off_again_after_a_reset()
    {
        Assert.That(await IsEnabledAsync("Tests.Off"), Is.False);

        Assert.That((await Service.SaveAsync("Tests.Off", new FlagSettings(true, null), null, default)).Outcome, Is.EqualTo(OverrideOutcome.Saved));
        Assert.That(await IsEnabledAsync("Tests.Off"), Is.True);
        Assert.That(await IsEnabledAsync("Tests.Off", variants: true), Is.True);

        Assert.That((await Service.ResetAsync("Tests.Off", default)).Outcome, Is.EqualTo(OverrideOutcome.Saved));
        Assert.That(await IsEnabledAsync("Tests.Off"), Is.False);
        Assert.That(await IsEnabledAsync("Tests.Off", variants: true), Is.False);
    }

    [Test]
    public async Task A_flag_declared_on_can_be_switched_off_and_a_reset_turns_it_back_on()
    {
        await Service.SetEnabledAsync("Tests.On", false, null, default);
        Assert.That(await IsEnabledAsync("Tests.On"), Is.False);

        await Service.ResetAsync("Tests.On", default);

        Assert.That(await IsEnabledAsync("Tests.On"), Is.True);
    }

    [Test]
    public async Task A_flag_declared_in_code_can_be_switched_on_and_reset()
    {
        await Service.SetEnabledAsync("Tests.InCode", true, null, default);
        Assert.That(await IsEnabledAsync("Tests.InCode"), Is.True);

        await Service.ResetAsync("Tests.InCode", default);

        Assert.That(await IsEnabledAsync("Tests.InCode"), Is.False);
    }

    [Test]
    public async Task A_save_is_in_the_database_with_who_made_it_and_a_reset_removes_it()
    {
        var editor = Guid.NewGuid();
        await Service.SetEnabledAsync("Tests.Off", true, editor, default);

        FeatureFlagOverride saved = (await Site.Services.GetRequiredService<IFeatureFlagOverrideStore>().GetAllAsync(default)).Single();
        Assert.That(saved, Has.Property(nameof(FeatureFlagOverride.FlagName)).EqualTo("Tests.Off")
            .And.Property(nameof(FeatureFlagOverride.UpdatedBy)).EqualTo(editor));

        await Service.ResetAsync("Tests.Off", default);

        Assert.That(await Site.Services.GetRequiredService<IFeatureFlagOverrideStore>().GetAllAsync(default), Is.Empty);
    }

    [Test]
    public async Task A_flag_nobody_declared_cannot_be_saved()
    {
        Assert.That((await Service.SaveAsync("Tests.Nobody", new FlagSettings(true, null), null, default)).Outcome, Is.EqualTo(OverrideOutcome.NotFound));
    }

    private async Task<bool> IsEnabledAsync(string flagName, bool variants = false)
    {
        await using AsyncServiceScope scope = Site.CreateScope();
        return variants
            ? await scope.ServiceProvider.GetRequiredService<IVariantFeatureManager>().IsEnabledAsync(flagName)
            : await scope.ServiceProvider.GetRequiredService<IFeatureManager>().IsEnabledAsync(flagName);
    }
}
