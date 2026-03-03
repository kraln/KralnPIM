using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using PIM.Core.Data;

namespace PIM.Search.Tests.TestHelpers;

public sealed class TestDb : IDisposable
{
    public DbConnectionFactory Factory { get; }
    private readonly SqliteConnection _keepAlive;

    public TestDb()
    {
        Factory = DbConnectionFactory.CreateForTesting();
        _keepAlive = Factory.CreateConnection();

        var runner = new MigrationRunner(Factory, NullLogger<MigrationRunner>.Instance);
        var sqlDir = FindSqlDirectory();
        runner.RunAsync(sqlDir).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
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
        throw new DirectoryNotFoundException("Could not find sql/ directory. Ensure the test is run from the repository.");
    }
}
