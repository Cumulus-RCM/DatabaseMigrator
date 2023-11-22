using System.Threading.Tasks;

namespace DatabaseMigrator;

public interface IMigrator {
    Task<bool> UpgradeDatabaseAsync();
}