using Microsoft.Extensions.Logging.Abstractions;
using PIM.Core.Data;

namespace PIM.Core.Tests.Data;

public class MigrationRunnerTests
{
    [Fact]
    public async Task RunAsync_FreshDatabase_AppliesAllMigrations()
    {
        var factory = DbConnectionFactory.CreateForTesting();
        using var keepAlive = factory.CreateConnection();
        var runner = new MigrationRunner(factory, NullLogger<MigrationRunner>.Instance);

        var sqlDir = FindSqlDirectory();
        await runner.RunAsync(sqlDir);

        using var conn = factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __migrations";
        var count = (long)(await cmd.ExecuteScalarAsync())!;

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task RunAsync_AlreadyApplied_SkipsMigrations()
    {
        var factory = DbConnectionFactory.CreateForTesting();
        using var keepAlive = factory.CreateConnection();
        var runner = new MigrationRunner(factory, NullLogger<MigrationRunner>.Instance);
        var sqlDir = FindSqlDirectory();

        await runner.RunAsync(sqlDir);
        await runner.RunAsync(sqlDir); // second run

        using var conn = factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM __migrations";
        var count = (long)(await cmd.ExecuteScalarAsync())!;

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task RunAsync_EmptyDirectory_NoOp()
    {
        var factory = DbConnectionFactory.CreateForTesting();
        using var keepAlive = factory.CreateConnection();
        var runner = new MigrationRunner(factory, NullLogger<MigrationRunner>.Instance);

        var emptyDir = Path.Combine(Path.GetTempPath(), $"pim_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);

        try
        {
            await runner.RunAsync(emptyDir);

            using var conn = factory.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM __migrations";
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.Equal(0, count);
        }
        finally
        {
            Directory.Delete(emptyDir, true);
        }
    }

    private static string FindSqlDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var sqlDir = Path.Combine(dir, "sql");
            if (Directory.Exists(sqlDir))
                return sqlDir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not find sql/ directory");
    }
}
