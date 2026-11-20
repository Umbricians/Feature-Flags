using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Umbricians.FeatureFlags.Composers;

/// <summary>
/// Registers Feature Flags with Umbraco.
/// </summary>
public class FeatureFlagsComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder) => builder.Services.AddFeatureFlags(builder.Config);
}
