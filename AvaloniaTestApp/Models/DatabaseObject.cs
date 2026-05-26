namespace AvaloniaTestApp.Models;

public enum DbObjectType
{
    Table,
    View
}

public class DatabaseObject
{
    public string Name { get; set; } = string.Empty;
    public DbObjectType Type { get; set; }

    public string IconKind => Type == DbObjectType.Table ? "Table" : "View";
    public override string ToString() => Name;
}