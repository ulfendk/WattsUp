using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace WattsUp.Data;

/// <summary>
/// Applies plain, versioned <c>.sql</c> scripts embedded from <c>Data/Migrations/</c> in order.
/// No EF/Fluent migrations — this is a handful of hand-written scripts, tracked by the
/// <c>schema_version</c> table each script itself inserts into.
/// </summary>
public sealed class DatabaseMigrator(ISqliteConnectionFactory connectionFactory, ILogger<DatabaseMigrator> logger)
{
    private static readonly Regex MigrationFileNamePattern = new(@"(\d+)_[^.]+\.sql$", RegexOptions.Compiled);

    public void Migrate()
    {
        using var connection = connectionFactory.CreateOpenConnection();

        using (var createVersionTable = connection.CreateCommand())
        {
            createVersionTable.CommandText =
                "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL PRIMARY KEY, applied_at TEXT NOT NULL);";
            createVersionTable.ExecuteNonQuery();
        }

        var currentVersion = GetCurrentVersion(connection);
        var migrations = LoadEmbeddedMigrations();

        foreach (var (version, name, sql) in migrations.OrderBy(m => m.Version))
        {
            if (version <= currentVersion)
            {
                continue;
            }

            logger.LogInformation("Applying migration {Version} ({Name})", version, name);

            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    private static int GetCurrentVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        var result = command.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    private static List<(int Version, string Name, string Sql)> LoadEmbeddedMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var migrations = new List<(int, string, string)>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            var match = MigrationFileNamePattern.Match(resourceName);
            if (!match.Success)
            {
                continue;
            }

            var version = int.Parse(match.Groups[1].Value);
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded migration resource '{resourceName}' could not be opened.");
            using var reader = new StreamReader(stream);
            migrations.Add((version, resourceName, reader.ReadToEnd()));
        }

        return migrations;
    }
}
