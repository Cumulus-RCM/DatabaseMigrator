namespace DatabaseMigrator;

public interface IMigrator {
    Task<bool> UpgradeDatabaseAsync();
}