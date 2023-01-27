using Dapper;
using DataAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoppingCart.DatabaseMigrator.Models;

namespace DatabaseMigrator;

public static class MigratorExt {
    public static Task<bool> ApplyDatabaseMigrationsAsync(this IServiceProvider serviceProvider ) {
        var migrator = serviceProvider.GetService<Migrator>() ?? throw new Exception("Unable to create Migrator.");
        return migrator.UpgradeDatabaseAsync();
    }
}

public class Migrator {
    private readonly IConfiguration config;
    private readonly ILogger<Migrator> logger;

    public Migrator(IConfiguration config, ILogger<Migrator> logger) {
        this.config = config;
        this.logger = logger;
    }

    public async Task<bool> UpgradeDatabaseAsync() {
        var connectionString = config["ConnectionStrings:DefaultConnection"] ?? throw new InvalidDataException("ConnectionString is not set");
        var cm = new DbConnectionManager(connectionString);

        using var conn = cm.CreateConnection();
        var maxId = await conn.ExecuteScalarAsync<int>("SELECT MAX(Id) FROM AppliedMigrationScript").ConfigureAwait(false);

        var migratorConnectionString = config["ConnectionStrings:MigratorConnection"] ?? throw new InvalidDataException("Migrator ConnectionString is not set");
        var migConnectionManager = new DbConnectionManager(migratorConnectionString);

        using var migConn = migConnectionManager.CreateConnection();
        var scripts = (await migConn.QueryAsync<MigrationScript>("SELECT * FROM MigrationScript WHERE Id > @Id ORDER by Id", new { Id = maxId }).ConfigureAwait(false)).ToList();
        if (!scripts.Any()) return true;

        using var transaction = conn.BeginTransaction();
        try {
            foreach (var script in scripts) {
                await conn.ExecuteAsync(script.Script, transaction:transaction).ConfigureAwait(false);
                await conn.ExecuteAsync("INSERT INTO AppliedMigrationScript (Script_Id) VALUES (@scriptId)", new {scriptId = script.Id}, transaction).ConfigureAwait(false);
                logger.LogInformation($"Applied Database Migration Script {script.Id} - {script.Description}");
            }
            transaction.Commit();
            return true;
        }
        catch (Exception e) {
            transaction.Rollback();
            logger.LogError(e, "Error Updating Database Schema.");
            return false;
        }
    }
}