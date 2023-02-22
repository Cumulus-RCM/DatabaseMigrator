namespace DatabaseMigrator; 

public class MigrationScript {
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public string Script { get; set; } = "";
    public uint ScriptOrder { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
}