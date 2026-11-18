using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NetCafeManager.Infrastructure.Persistence;

public sealed class NetCafeDbContextFactory : IDesignTimeDbContextFactory<NetCafeDbContext>
{
    private const string ConnectionStringEnvironmentVariable = "NETCAFE_DB_CONNECTION";

    public NetCafeDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=NetCafeManager;"
                + "Trusted_Connection=True;TrustServerCertificate=True";

        DbContextOptions<NetCafeDbContext> options =
            new DbContextOptionsBuilder<NetCafeDbContext>()
                .UseSqlServer(connectionString)
                .Options;

        return new NetCafeDbContext(options);
    }
}
