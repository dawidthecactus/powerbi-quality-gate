using System.Text.RegularExpressions;

namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// Counts explicitly declared TMDL objects in a well-formed model folder.
/// It deliberately does not parse DAX, infer relationships, or certify TMDL validity.
/// </summary>
public sealed class TmdlInventoryReader
{
    private static readonly Regex TableDeclaration = new(
        @"^table\s+(?<name>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ChildDeclaration = new(
        @"^(?<type>measure|column|calculatedColumn|calculatedTableColumn|hierarchy)\s+\S",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex RelationshipDeclaration = new(
        @"^relationship\s+\S", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public TmdlInventory Read(string definitionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionDirectory);
        if (!Directory.Exists(definitionDirectory))
        {
            throw new DirectoryNotFoundException($"TMDL definition directory not found: {definitionDirectory}");
        }

        var tablesDir = Path.Combine(definitionDirectory, "tables");
        var tables = new List<TmdlTableInventory>();
        var files = 0;
        if (Directory.Exists(tablesDir))
        {
            foreach (var file in Directory.EnumerateFiles(tablesDir, "*.tmdl", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                files++;
                tables.Add(ReadTable(file));
            }
        }

        var relationships = 0;
        var relationshipFile = Path.Combine(definitionDirectory, "relationships.tmdl");
        if (File.Exists(relationshipFile))
        {
            files++;
            foreach (var line in File.ReadLines(relationshipFile))
            {
                if (line.Length > 0 && !char.IsWhiteSpace(line[0]) &&
                    RelationshipDeclaration.IsMatch(line.TrimEnd()))
                {
                    relationships++;
                }
            }
        }

        return new TmdlInventory(tables, relationships, files);
    }

    private static TmdlTableInventory ReadTable(string file)
    {
        string? name = null;
        int? objectIndent = null;
        var columns = 0;
        var measures = 0;
        var hierarchies = 0;
        foreach (var line in File.ReadLines(file))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var indent = line.Length - line.TrimStart(' ', '\t').Length;
            var content = line[indent..];
            if (indent == 0)
            {
                var table = TableDeclaration.Match(content);
                if (table.Success)
                {
                    if (name is not null)
                    {
                        throw new FormatException($"Multiple table declarations in {file}.");
                    }
                    name = Unquote(table.Groups["name"].Value);
                }
                continue;
            }

            if (name is null)
            {
                continue;
            }

            var child = ChildDeclaration.Match(content);
            if (!child.Success)
            {
                continue;
            }

            // Child objects are all direct descendants of a table. Nested DAX/M
            // expression text is not counted unless it appears at that same level.
            if (objectIndent is null)
            {
                objectIndent = indent;
            }
            if (indent != objectIndent)
            {
                continue;
            }

            switch (child.Groups["type"].Value.ToLowerInvariant())
            {
                case "measure":
                    measures++;
                    break;
                case "column":
                case "calculatedcolumn":
                case "calculatedtablecolumn":
                    columns++;
                    break;
                case "hierarchy":
                    hierarchies++;
                    break;
            }
        }

        if (name is null)
        {
            throw new FormatException($"Missing top-level table declaration in {file}.");
        }

        return new TmdlTableInventory(name, columns, measures, hierarchies, Path.GetFileName(file));
    }

    private static string Unquote(string name)
    {
        if (name.Length >= 2 && name.StartsWith('\'') && name.EndsWith('\''))
        {
            return name[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }
        if (name.Length >= 2 && name.StartsWith('"') && name.EndsWith('"'))
        {
            return name[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        }
        return name;
    }
}
