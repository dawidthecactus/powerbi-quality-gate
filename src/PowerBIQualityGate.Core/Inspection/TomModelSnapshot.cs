namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// Immutable, read-only projection of objects deserialized by Microsoft TOM.
/// DAX expressions are reported as metadata and are never executed or validated.
/// </summary>
public sealed record TomColumnSnapshot(
    string Name,
    string DataType,
    string Kind,
    bool IsHidden,
    string? Expression);

public sealed record TomMeasureSnapshot(
    string Name,
    string Expression,
    string? FormatString,
    bool IsHidden,
    string DataType = "Unknown",
    bool HasDynamicFormatString = false);

public sealed record TomTableSnapshot(
    string Name,
    bool IsHidden,
    IReadOnlyList<TomColumnSnapshot> Columns,
    IReadOnlyList<TomMeasureSnapshot> Measures,
    int Hierarchies,
    int Partitions);

public sealed record TomRelationshipSnapshot(
    string Name,
    string From,
    string To,
    bool IsActive,
    string CrossFilterDirection);

public sealed record TomModelSnapshot(
    string Name,
    int CompatibilityLevel,
    IReadOnlyList<TomTableSnapshot> Tables,
    IReadOnlyList<TomRelationshipSnapshot> Relationships)
{
    public int Columns => Tables.Sum(table => table.Columns.Count);
    public int Measures => Tables.Sum(table => table.Measures.Count);
    public int Hierarchies => Tables.Sum(table => table.Hierarchies);
    public int Partitions => Tables.Sum(table => table.Partitions);
}
