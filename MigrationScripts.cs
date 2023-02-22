using System.Text.Json;

namespace DatabaseMigrator;

public class MigrationScripts {
    public static async Task<IEnumerable<MigrationScript>> GetScriptsAsync(Stream migrationsStream) {
        if (migrationsStream.Length < 10) return Enumerable.Empty<MigrationScript>();
        var scripts = await JsonSerializer.DeserializeAsync<IEnumerable<MigrationScript>>(migrationsStream).ConfigureAwait(false);
        return scripts ?? Enumerable.Empty<MigrationScript>();
        //return new List<MigrationScript>() {
        //    new() {
        //        Id = 1,
        //        Created = DateTime.Now,
        //        Description = "Test",
        //        Script = "Do some Stuff",
        //        ScriptOrder = 1
        //    },
        //    new() {
        //        Id = 2,
        //        Created = DateTime.Now,
        //        Description = "Test 2",
        //        Script = "Do some other Stuff",
        //        ScriptOrder = 2
        //    }
        //};
    }

    public static Task SaveScriptsAsync(Stream migrationsStream, IEnumerable<MigrationScript> migrationScripts) =>
        JsonSerializer.SerializeAsync(migrationsStream, migrationScripts);
}