using BaseLib;
using Dapper;
using Microsoft.Extensions.Logging;

namespace DatabaseMigrator;

public class Migrator {
    private readonly IDbConnectionManager connectionManager;
    private readonly ILogger<Migrator> logger;
    // ReSharper disable once InconsistentNaming
    private const string APPLIED_MIGRATION_SCRIPT_SQL = 
        @"IF OBJECT_ID(N'dbo.AppliedMigrationScript', 'U') IS NULL
   CREATE TABLE dbo.AppliedMigrationScript (
      ScriptName varchar(500) NOT NULL,
      AppliedOn smalldatetime NOT NULL DEFAULT (getutcdate()),
   CONSTRAINT PK_AppliedMigrationScript_ScriptName PRIMARY KEY CLUSTERED (ScriptName));
SELECT ScriptName FROM AppliedMigrationScript";

    public Migrator(IDbConnectionManager connectionManager, ILoggerFactory loggerFactory) {
        this.connectionManager = connectionManager;
        this.logger = loggerFactory.CreateLogger<Migrator>();
    }

    public async Task<bool> UpgradeDatabaseAsync() {
        using var conn = connectionManager.CreateConnection();
        var appliedScriptNames = (await conn.QueryAsync<string>(APPLIED_MIGRATION_SCRIPT_SQL).ConfigureAwait(false)).ToList();
        var path = Path.Combine( AppDomain.CurrentDomain.BaseDirectory, "MigrationScripts");
        var migrationScripts = await getScriptsAsync(path, appliedScriptNames);
        if (!migrationScripts.Any()) return true;

        using var transaction = conn.BeginTransaction();
        var currentScriptName = "";
        try {
            foreach (var migrationScript in migrationScripts) {
                currentScriptName = migrationScript.scriptName;
                await conn.ExecuteAsync(migrationScript.script, transaction: transaction).ConfigureAwait(false);
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

        static async Task<ICollection<(string scriptName, string script)>> getScriptsAsync(string migrationScriptPath, ICollection<string> alreadyAppliedScripts) {
            var scriptNames = Directory.EnumerateFiles(migrationScriptPath, "*.sql")
                .Where(filename => !alreadyAppliedScripts.Contains(filename))
                .OrderBy(filename => filename);
            var result = new List<(string, string)>();
            foreach (var scriptName in scriptNames) {
                result.Add((scriptName, await File.ReadAllTextAsync(scriptName)));
            }
            return result;
        }
    }
}