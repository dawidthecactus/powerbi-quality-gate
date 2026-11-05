namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// Lightweight, read-only inventory of visible TMDL declarations.
/// This is not a TMDL grammar parser or semantic validation.
/// </summary>
public sealed record TmdlTableInventory(
    string Name,
    int Columns,
    int Measures,
    int Hierarchies,
    string SourceFile);

public sealed record TmdlInventory(
    IReadOnlyList<TmdlTableInventory> Tables,
    int Relationships,
    int FilesRead)
{
    public int Columns => Tables.Sum(table => table.Columns);
    public int Measures => Tables.Sum(table => table.Measures);
    public int Hierarchies => Tables.Sum(table => table.Hierarchies);
}
