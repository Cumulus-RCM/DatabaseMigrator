using BaseLib;
using Dapper;
using Microsoft.Extensions.Logging;

namespace DatabaseMigrator;

public class Migrator {
    private readonly DbConnectionManager connectionManager;
    private readonly ILogger<Migrator> logger;
    private const string APPLIED_MIGRATION_SCRIPT_SQL = 
        @"IF OBJECT_ID(N'dbo.AppliedMigrationScript', 'U') IS NULL
   CREATE TABLE dbo.AppliedMigrationScript (
      ScriptName varchar(500) NOT NULL,
      AppliedOn smalldatetime NOT NULL DEFAULT (getutcdate()),
   CONSTRAINT PK_AppliedMigrationScript_ScriptName PRIMARY KEY CLUSTERED (ScriptName));
SELECT ScriptName FROM AppliedMigrationScript";

    public Migrator(DbConnectionManager connectionManager, ILoggerFactory loggerFactory) {
        this.connectionManager = connectionManager;
        this.logger = loggerFactory.CreateLogger<Migrator>();
    }

    public async Task<bool> UpgradeDatabaseAsync() {
        using var conn = connectionManager.CreateConnection();
        var appliedScriptNames = await conn.QueryAsync<string>(APPLIED_MIGRATION_SCRIPT_SQL).ConfigureAwait(false);
        var path = Path.Combine( AppDomain.CurrentDomain.BaseDirectory, "MigrationScripts");
        var migrationScripts = getScripts(path, appliedScriptNames.ToList());
        if (!migrationScripts.Any()) return true;
                  
        using var transaction = conn.BeginTransaction();
        var currentScriptName = "";
        try {
            foreach (var migrationScript in migrationScripts) {
                currentScriptName = migrationScript.scriptName;
                await conn.ExecuteAsync(migrationScript.script, transaction:transaction).ConfigureAwait(false);
                await conn.ExecuteAsync("INSERT INTO AppliedMigrationScript (ScriptName) VALUES (@scriptName)", new {migrationScript.scriptName}, transaction).ConfigureAwait(false);
                logger.LogInformation($"Applied Database Migration Script: {migrationScript.scriptName}.");
            }
            transaction.Commit();
            return true;
        }
        catch (Exception e) {
            transaction.Rollback();
            logger.LogError(e, $"Error Applying Migration Script {currentScriptName}.");
            return false;
        }
    }

    private static ICollection<(string scriptName, string script)> getScripts(string path, ICollection<string> appliedScriptNames) {
        var scripts = MigrationScripts.GetScripts(path);
        return scripts
            .Where(s => appliedScriptNames.All(appliedScriptName => appliedScriptName != s.scriptName))
            .ToList();
        }
}