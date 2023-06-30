﻿using System.Text.Json;
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
      Script_Id int NOT NULL,
      AppliedOn smalldatetime NOT NULL DEFAULT (getutcdate()),
   CONSTRAINT PK_AppliedMigrationScript PRIMARY KEY CLUSTERED (Script_Id));
SELECT Script_Id FROM AppliedMigrationScript";

    public Migrator(IDbConnectionManager connectionManager, ILoggerFactory loggerFactory) {
        this.connectionManager = connectionManager;
        this.logger = loggerFactory.CreateLogger<Migrator>();
    }

    // ReSharper disable PossibleMultipleEnumeration
    public async Task<bool> UpgradeDatabaseAsync() {
        using var conn = connectionManager.CreateConnection();
        var appliedScripts = await conn.QueryAsync<int>(APPLIED_MIGRATION_SCRIPT_SQL).ConfigureAwait(false);
        var path = Path.Combine( AppDomain.CurrentDomain.BaseDirectory, "MigrationScripts\\MigrationScripts.json");
        if (!File.Exists(path)) return true;
        var json = await File.ReadAllTextAsync(path);
        var migrationScripts = JsonSerializer.Deserialize<IEnumerable<MigrationScript>>(json)!.Where(ms => ms.IsActive && !appliedScripts.Contains(ms.Id));
        if (!migrationScripts.Any()) return true;

        using var transaction = conn.BeginTransaction();
        var currentScriptName = "";
        try {
           
            foreach (var migrationScript in migrationScripts.Where(ms=>ms.IsActive).OrderBy(ms=>ms.ScriptOrder)) {
                await conn.ExecuteAsync(migrationScript.Script, transaction: transaction).ConfigureAwait(false);
                await conn.ExecuteAsync("INSERT INTO AppliedMigrationScript (Script_Id) VALUES (@Id)", new {migrationScript.Id}, transaction).ConfigureAwait(false);
                logger.LogInformation($"Applied Database Migration Script: {migrationScript.Id}, {migrationScript.Description}.");
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
    // ReSharper restore PossibleMultipleEnumeration
}