using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.FeatureManagement;
using Umbraco.Cms.Core.Composing;
using Umbricians.FeatureFlags.Extensions;

namespace Umbricians.FeatureFlags.Demo.DeveloperExtension;

/// <summary>
/// Example developer extension: registers the culture condition's two C# halves. Its backoffice half (the condition's
/// editor, an entity action and a workspace view) is in <c>wwwroot/App_Plugins/FeatureFlagsDemo</c>. Demo only.
/// </summary>
public class DeveloperExtensionComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Feature Flags has already called AddFeatureManagement(), and calling it again from a composer registers
        // Microsoft's filters twice, so the filter is added the way AddFeatureFilter<T>() adds it.
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFeatureFilterMetadata, CultureFilter>());
        builder.AddFeatureFlags(flags => flags.AddCondition<CultureCondition>());
    }
}
