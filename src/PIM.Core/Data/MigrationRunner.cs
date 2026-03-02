using Microsoft.Extensions.Logging;

namespace PIM.Core.Data;

public sealed class MigrationRunner
{
    private readonly DbConnectionFactory _factory;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(DbConnectionFactory factory, ILogger<MigrationRunner> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task RunAsync(string sqlDirectory, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();

        using var createCmd = conn.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS __migrations (
                filename TEXT PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
            """;
        await createCmd.ExecuteNonQueryAsync(ct);

        var applied = new HashSet<string>();
        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT filename FROM __migrations";
        using var reader = await selectCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            applied.Add(reader.GetString(0));
        reader.Close();

        var files = Directory.GetFiles(sqlDirectory, "*.sql")
            .OrderBy(Path.GetFileName)
            .ToList();

        foreach (var file in files)
        {
            var filename = Path.GetFileName(file);
            if (applied.Contains(filename))
                continue;

            _logger.LogInformation("Applying migration: {Filename}", filename);
            var sql = await File.ReadAllTextAsync(file, ct);

            using var transaction = conn.BeginTransaction();
            using var execCmd = conn.CreateCommand();
            execCmd.Transaction = transaction;
            execCmd.CommandText = sql;
            await execCmd.ExecuteNonQueryAsync(ct);

            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = "INSERT INTO __migrations (filename, applied_at) VALUES (@f, @t)";
            insertCmd.Parameters.AddWithValue("@f", filename);
            insertCmd.Parameters.AddWithValue("@t", DateTimeOffset.UtcNow.ToString("O"));
            await insertCmd.ExecuteNonQueryAsync(ct);

            transaction.Commit();
            _logger.LogInformation("Applied migration: {Filename}", filename);
        }
    }
}
