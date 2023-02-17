namespace DatabaseMigrator; 

public class MigrationScript {
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public string Script { get; set; } = "";
    public int ScriptOrder { get; set; }
    public string? MinimumApplicationVersion { get; set; }
    public string? MaximumApplicationVersion { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
}