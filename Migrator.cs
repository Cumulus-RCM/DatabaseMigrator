using Dapper;
using DataAccess;
using Microsoft.Extensions.Logging;

namespace DatabaseMigrator;

public class Migrator {
    private const string APPLIED_MIGRATION_SCRIPT_SQL = 
        @"IF OBJECT_ID(N'dbo.AppliedMigrationScript', 'U') IS NULL
   CREATE TABLE dbo.AppliedMigrationScript (
      ScriptName varchar(500) NOT NULL,
      AppliedOn smalldatetime NOT NULL DEFAULT (getutcdate()),
   CONSTRAINT PK_AppliedMigrationScript_ScriptName PRIMARY KEY CLUSTERED (ScriptName));
SELECT ScriptName FROM AppliedMigrationScript";
    private readonly DbConnectionManager connectionManager;
    private readonly ILogger<Migrator> logger;

    public Migrator(DbConnectionManager connectionManager, ILoggerFactory loggerFactory) {
        this.connectionManager = connectionManager;
        this.logger = loggerFactory.CreateLogger<Migrator>();
    }

    public async Task<bool> UpgradeDatabaseAsync(string path) {
        using var conn = connectionManager.CreateConnection();
        var appliedScriptNames = await conn.QueryAsync<string>(APPLIED_MIGRATION_SCRIPT_SQL).ConfigureAwait(false);
        var scriptNames = getScripts(path, appliedScriptNames.ToList());
        if (!scriptNames.Any()) return true;
                  
        using var transaction = conn.BeginTransaction();
        var currentScriptName = "";
        try {
            foreach (var scriptName in scriptNames) {
                currentScriptName = scriptName;
                await conn.ExecuteAsync(scriptName, transaction:transaction).ConfigureAwait(false);
                await conn.ExecuteAsync("INSERT INTO AppliedMigrationScript (ScriptName) VALUES (@scriptName)", new {scriptName}, transaction).ConfigureAwait(false);
                logger.LogInformation($"Applied Database Migration Script: {scriptName}.");
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

    private static ICollection<string> getScripts(string path, ICollection<string> appliedScriptNames) {
        var scriptNames = MigrationScripts.GetScripts(path);
        return scriptNames
            .Where(scriptName => appliedScriptNames.All(appliedScriptName => appliedScriptName != scriptName))
            .ToList();
        }
}