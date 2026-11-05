using Microsoft.AnalysisServices.Tabular;

namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// Loads a local TMDL definition using the official Microsoft TOM serializer.
/// No connection to Fabric/Power BI is opened and no metadata is saved.
/// </summary>
public sealed class TomModelReader
{
    public TomModelSnapshot Read(string definitionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionDirectory);

        if (!Directory.Exists(definitionDirectory))
        {
            throw new DirectoryNotFoundException($"TMDL definition directory not found: {definitionDirectory}");
        }

        var database = TmdlSerializer.DeserializeDatabaseFromFolder(definitionDirectory);
        var model = database.Model ??
                    throw new FormatException("TMDL deserialization returned a database without a semantic model.");

        var tables = model.Tables
            .OrderBy(table => table.Name, StringComparer.OrdinalIgnoreCase)
            .Select(table => new TomTableSnapshot(
                table.Name,
                table.IsHidden,
                table.Columns
                    .OrderBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(column => new TomColumnSnapshot(
                        column.Name,
                        column.DataType.ToString(),
                        column.Type.ToString(),
                        column.IsHidden,
                        column is CalculatedColumn calculated ? calculated.Expression : null))
                    .ToArray(),
                table.Measures
                    .OrderBy(measure => measure.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(measure => new TomMeasureSnapshot(
                        measure.Name,
                        measure.Expression ?? string.Empty,
                        measure.FormatString,
                        measure.IsHidden,
                        measure.DataType.ToString(),
                        measure.FormatStringDefinition is not null))
                    .ToArray(),
                table.Hierarchies.Count,
                table.Partitions.Count))
            .ToArray();

        var relationships = model.Relationships
            .OrderBy(relationship => relationship.Name, StringComparer.OrdinalIgnoreCase)
            .Select(relationship => new TomRelationshipSnapshot(
                relationship.Name,
                relationship is SingleColumnRelationship from
                    ? Endpoint(from.FromTable?.Name, from.FromColumn?.Name)
                    : relationship.FromTable?.Name ?? "?",
                relationship is SingleColumnRelationship to
                    ? Endpoint(to.ToTable?.Name, to.ToColumn?.Name)
                    : relationship.ToTable?.Name ?? "?",
                relationship.IsActive,
                relationship.CrossFilteringBehavior.ToString()))
            .ToArray();

        return new TomModelSnapshot(model.Name, database.CompatibilityLevel, tables, relationships);
    }

    private static string Endpoint(string? table, string? column) =>
        $"{table ?? "?"}[{column ?? "?"}]";
}
