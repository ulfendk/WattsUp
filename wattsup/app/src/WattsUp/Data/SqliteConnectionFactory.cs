using Microsoft.Data.Sqlite;

namespace WattsUp.Data;

public interface ISqliteConnectionFactory
{
    /// <summary>Opens a new WAL-mode connection. Callers own disposal.</summary>
    SqliteConnection CreateOpenConnection();
}

/// <summary>
/// Creates connections to <c>/data/wattsup.db</c> (or a configured path for local dev/tests),
/// enabling WAL mode for safe concurrent reads from background pollers + the UI/MQTT publisher.
/// </summary>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IConfiguration configuration)
    {
        var dbPath = configuration["WattsUp:DatabasePath"] ?? ResolveDefaultPath();
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    private static string ResolveDefaultPath()
    {
        // /data is the HA add-on's persistent volume. Fall back to a local file for dev/tests.
        return Directory.Exists("/data")
            ? "/data/wattsup.db"
            : Path.Combine(AppContext.BaseDirectory, "wattsup.db");
    }

    public SqliteConnection CreateOpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }
        return connection;
    }
}
