using System.Text.RegularExpressions;

namespace Umbricians.FeatureFlags.Tests.Security;

/// <summary>The backoffice and the server must name the same permissions.</summary>
public partial class ClientPermissionsTests
{
    [Test]
    public void The_client_lists_exactly_the_server_permissions()
    {
        string constants = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Umbricians.FeatureFlags", "Client", "src", "permissions", "constants.ts"));

        string[] clientPermissions = PermissionConstant().Matches(constants).Select(match => match.Groups[1].Value).ToArray();

        Assert.That(clientPermissions, Is.EquivalentTo(Constants.Permissions.All));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Umbricians.FeatureFlags.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Couldn't find the repository root.");
    }

    [GeneratedRegex("""export const FEATURE_FLAGS_PERMISSION_(?:VIEW|MANAGE|[A-Z_]+?) = "(Umbricians\.FeatureFlags\.[A-Za-z]+)";""")]
    private static partial Regex PermissionConstant();
}
