using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BaseLib;
using Dapper;
using Microsoft.Extensions.Logging;

namespace DatabaseMigrator;

public class Migrator(IDbConnectionManager manager, ILoggerFactory loggerFactory) : IMigrator {
    private readonly ILogger<Migrator> logger = loggerFactory.CreateLogger<Migrator>();
    // ReSharper disable once InconsistentNaming
    private const string APPLIED_MIGRATION_SCRIPT_SQL = 
        @"IF OBJECT_ID(N'dbo.AppliedMigrationScript', 'U') IS NULL
   CREATE TABLE dbo.AppliedMigrationScript (
      Script_Id int NOT NULL,
      AppliedOn smalldatetime NOT NULL DEFAULT (getutcdate()),
   CONSTRAINT PK_AppliedMigrationScript PRIMARY KEY CLUSTERED (Script_Id));
SELECT Script_Id FROM AppliedMigrationScript";

    // ReSharper disable PossibleMultipleEnumeration
    public async Task<bool> UpgradeDatabaseAsync() {
        using var conn = manager.CreateConnection();
        var appliedScripts = await conn.QueryAsync<int>(APPLIED_MIGRATION_SCRIPT_SQL).ConfigureAwait(false);
        var path = Path.Combine( AppDomain.CurrentDomain.BaseDirectory, "MigrationScripts\\MigrationScripts.json");
        if (!File.Exists(path)) return true;
        var json = await File.ReadAllTextAsync(path);
        var migrationScripts = JsonSerializer.Deserialize<IEnumerable<MigrationScript>>(json)!.Where(ms => ms.IsActive && !appliedScripts.Contains(ms.Id));
        if (!migrationScripts.Any()) return true;

        using var transaction = conn.BeginTransaction();
        MigrationScript? currentScript = null;
        try {
           
            foreach (var migrationScript in migrationScripts.Where(ms=>ms.IsActive).OrderBy(ms=>ms.ScriptOrder)) {
                currentScript = migrationScript;
                foreach (var script in migrationScript.Script) {
                    await conn.ExecuteAsync(script, transaction: transaction).ConfigureAwait(false);
                }

                await conn.ExecuteAsync("INSERT INTO AppliedMigrationScript (Script_Id) VALUES (@Id)", new {migrationScript.Id}, transaction).ConfigureAwait(false);
                logger.LogInformation("Applied Database Migration Script: {scriptId}, {scriptDescription}.", migrationScript.Id, migrationScript.Description);
            }

            transaction.Commit();
            return true;
        }
        catch (Exception e) {
            transaction.Rollback();
            logger.LogError(e, "Error Applying Migration Script {scriptDescription}.", currentScript?.Description);
            return false;
        }
    }
    // ReSharper restore PossibleMultipleEnumeration
}