namespace Umbricians.FeatureFlags.Controllers;

/// <summary>
/// The <c>type</c> every Feature Flags problem carries. The backoffice's HTTP client only passes a problem's own detail
/// on when it has a type; without one, an editor sees Umbraco's generic "fatal server error" instead of the reason.
/// </summary>
internal static class ProblemTypes
{
    public const string Error = "Error";
}
