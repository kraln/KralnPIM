using Microsoft.Data.Sqlite;

namespace PIM.Core.Data;

public sealed class DbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(string dbPath)
    {
        var expandedPath = dbPath.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var dir = Path.GetDirectoryName(expandedPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = expandedPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    private DbConnectionFactory(string connectionString, bool _)
    {
        _connectionString = connectionString;
    }

    public static DbConnectionFactory CreateForTesting()
    {
        var name = $"file:test_{Guid.NewGuid():N}?mode=memory&cache=shared";
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = name,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        return new DbConnectionFactory(connStr, true);
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return conn;
    }
}
