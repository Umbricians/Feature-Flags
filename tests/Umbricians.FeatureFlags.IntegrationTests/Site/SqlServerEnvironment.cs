namespace Umbricians.FeatureFlags.IntegrationTests.Site;

/// <summary>
/// Finds the SQL Server the tests run against: the connection string in the environment, or else the one
/// build/sql-server.sh keeps in tests/sqlserver.local.env. The connection string holds a password, so nothing here or in
/// the tests ever shows it.
/// </summary>
internal static class SqlServerEnvironment
{
    public const string Variable = "FEATUREFLAGS_SQLSERVER";

    public const string HowToStart =
        "No SQL Server to test against. Run build/sql-server.sh to start one in Docker, or set FEATUREFLAGS_SQLSERVER to the connection string of a server.";

    private const string FileName = "sqlserver.local.env";

    /// <summary>The server-level connection string, or null when there is neither the variable nor the file.</summary>
    public static string? FindConnectionString()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(Variable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        string? file = FindFile();
        return file is null ? null : ReadVariable(File.ReadAllLines(file));
    }

    private static string? FindFile()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string file = Path.Combine(directory.FullName, FileName);
            if (File.Exists(file))
            {
                return file;
            }
        }

        return null;
    }

    private static string? ReadVariable(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(Variable + "=", StringComparison.Ordinal))
            {
                string value = trimmed[(Variable.Length + 1)..].Trim();
                if (value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\'')
                {
                    value = value[1..^1];
                }

                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }
}
