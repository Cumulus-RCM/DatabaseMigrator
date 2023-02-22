using Dapper;
using DataAccess;
using Microsoft.Extensions.Logging;

namespace DatabaseMigrator;

public class Migrator {
    private const string APPLIED_MIGRATION_SCRIPT_SQL = 
        @"IF OBJECT_ID(N'dbo.AppliedMigrationScript', 'U') IS NULL
   CREATE TABLE dbo.AppliedMigrationScript (
      MigrationScript_Id int NOT NULL,
      Applied smalldatetime NOT NULL DEFAULT (getutcdate()),
   CONSTRAINT PK_AppliedMigrationScript_MigrationScript_Id PRIMARY KEY CLUSTERED (MigrationScript_Id));
SELECT MigrationScript_Id FROM AppliedMigrationScript";
    // ReSharper disable InconsistentNaming
    private readonly Version VERSION_MIN = new ("0.0");
    private readonly Version VERSION_MAX = new ("999.999.999");
    // ReSharper restore InconsistentNaming

    private readonly DbConnectionManager connectionManager;
    private readonly ILogger<Migrator> logger;

    public Migrator(DbConnectionManager connectionManager, ILoggerFactory loggerFactory) {
        this.connectionManager = connectionManager;
        this.logger = loggerFactory.CreateLogger<Migrator>();
    }

    public async Task<bool> UpgradeDatabaseAsync(Version? appVersion, Stream migrationsStream) {
        using var conn = connectionManager.CreateConnection();
        var appliedIds = await conn.QueryAsync<int>(APPLIED_MIGRATION_SCRIPT_SQL).ConfigureAwait(false);
        var scripts = await getScriptsAsync(migrationsStream, appliedIds).ConfigureAwait(false);
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

    private static async Task<ICollection<MigrationScript>> getScriptsAsync(Stream migrationsStream, IEnumerable<int> appliedIds) {
        var scripts = await MigrationScripts.GetScriptsAsync(migrationsStream).ConfigureAwait(false);
        return scripts.Where(s=> appliedIds.All( a=> a != s.Id)).ToList();
    }
}