using System.Reflection;
using Dapper;
using DataAccess;
using DatabaseMigrator.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DatabaseMigrator;

public static class MigratorExt {
    public static Task<bool> ApplyDatabaseMigrationsAsync(this IServiceProvider serviceProvider ) {
        var migrator = serviceProvider.GetService<Migrator>() ?? throw new Exception("Unable to create Migrator.");
        return migrator.UpgradeDatabaseAsync(Assembly.GetEntryAssembly()?.GetName().Version);
    }
}

public class Migrator {
    private const string APPLIED_MIGRATION_SCRIPT_SQL = 
        @"IF OBJECT_ID(N'dbo.AppliedMigrationScript', 'U') IS NULL
           CREATE TABLE dbo.AppliedMigrationScript (
              MigrationScript_Id int NOT NULL,
              Applied smalldatetime NOT NULL DEFAULT (getutcdate()),
           CONSTRAINT PK_AppliedMigrationScript_MigrationScript_Id PRIMARY KEY CLUSTERED (MigrationScript_Id);

         SELECT MigrationScript_Id FROM AppliedMigrationScript";
    // ReSharper disable InconsistentNaming
    private readonly Version VERSION_MIN = new ("0.0");
    private readonly Version VERSION_MAX = new ("999.999.999");
    // ReSharper restore InconsistentNaming

    private readonly IConfiguration config;
    private readonly ILogger<Migrator> logger;

    public Migrator(IConfiguration config, ILogger<Migrator> logger) {
        this.config = config;
        this.logger = logger;
    }

    public async Task<bool> UpgradeDatabaseAsync(Version? appVersion) {
        var connectionString = config["ConnectionStrings:DefaultConnection"] ?? throw new InvalidDataException("ConnectionString is not set");
        var cm = new DbConnectionManager(connectionString);
        
        using var conn = cm.CreateConnection();
        var appliedIds = await conn.QueryAsync<int>(APPLIED_MIGRATION_SCRIPT_SQL).ConfigureAwait(false);

        var migratorConnectionString = config["ConnectionStrings:MigratorConnection"] ?? throw new InvalidDataException("Migrator ConnectionString is not set");
        var migConnectionManager = new DbConnectionManager(migratorConnectionString);
        
        using var migConn = migConnectionManager.CreateConnection();
        var scripts = (await migConn.QueryAsync<MigrationScript>("SELECT * FROM MigrationScript WHERE Id NOT IN @appliedIds ORDER by [Order]", new { appliedIds }).ConfigureAwait(false)).ToList();
        if (!scripts.Any()) return true;
                  
        using var transaction = conn.BeginTransaction();
        try {
            foreach (var script in scripts.Where(shouldApplyScript)) {
                await conn.ExecuteAsync(script.Script, transaction:transaction).ConfigureAwait(false);
                await conn.ExecuteAsync("INSERT INTO AppliedMigrationScript (Script_Id) VALUES (@scriptId)", new {scriptId = script.Id}, transaction).ConfigureAwait(false);
                logger.LogInformation($"Applied Database Migration Script (Id:{script.Id}) {script.Description}");
            }
            transaction.Commit();
            return true;
        }
        catch (Exception e) {
            transaction.Rollback();
            logger.LogError(e, "Error Updating Database Schema.");
            return false;
        }

        bool shouldApplyScript(MigrationScript migrationScript) {
            var minApplyVersion = migrationScript.MinimumApplicationVersion is null
                ? VERSION_MIN
                : new Version(migrationScript.MinimumApplicationVersion);
            var maxApplyVersion = migrationScript.MaximumApplicationVersion is null 
                ? VERSION_MAX
                : new Version(migrationScript.MaximumApplicationVersion);
            return appVersion > minApplyVersion && appVersion <= maxApplyVersion;
        }
    }
}