using System;

namespace DatabaseMigrator; 

public class MigrationScript {
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public string[] Script { get; set; } = null!;
    public int ScriptOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
}