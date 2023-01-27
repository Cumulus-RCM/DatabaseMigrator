namespace ShoppingCart.DatabaseMigrator.Models; 

public class AppliedMigrationScript {
    public int Id { get; set; }
    public int Script_Id { get; set; }
    public DateTime Applied { get; set; }
}