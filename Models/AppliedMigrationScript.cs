﻿namespace DatabaseMigrator.Models; 

public class AppliedMigrationScript {
    public int MigrationScript_Id { get; set; }
    public DateTime Applied { get; set; }
}