using System.Text.Json.Nodes;

namespace Umbricians.FeatureFlags.Flags;

/// <summary>
/// A condition a developer has registered with <c>AddCondition&lt;T&gt;()</c>: the half of it that runs in C#. The
/// other half is Microsoft's feature filter that evaluates it, registered the usual Microsoft way. A flag that uses
/// only registered conditions shows as <see cref="FeatureFlagState.Conditions"/> and can be switched on and off in the
/// backoffice; a flag that uses any other filter is <see cref="FeatureFlagState.Custom"/>. See <c>docs/conditions.md</c>.
/// </summary>
/// <remarks>
/// One instance is shared by every request, so it must be safe to call from several threads. Its methods are called
/// with a copy of the condition's parameters, and should not throw.
/// </remarks>
public interface IFeatureFlagCondition
{
    /// <summary>
    /// The name of the Microsoft filter this edits, such as <c>Contoso.Culture</c>. Flags written with a shorter name
    /// still match, the way Microsoft matches filter names: <c>Culture</c> matches <c>Contoso.Culture</c>, and case is
    /// ignored.
    /// </summary>
    string Name { get; }

    /// <summary>What editors call the condition, such as "Culture".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Problems with the parameters an editor or code is saving, in plain English for the editor to read; none means
    /// they are valid. Called on every save, whether from the backoffice or from <see cref="Overrides.IFeatureFlagService"/>.
    /// </summary>
    /// <param name="parameters">The filter's parameters as JSON.</param>
    IEnumerable<string> Validate(JsonObject parameters);

    /// <summary>A one-line summary of the parameters for the flag list, such as "French and German".</summary>
    /// <param name="parameters">The filter's parameters as JSON.</param>
    string Describe(JsonObject parameters);
}
