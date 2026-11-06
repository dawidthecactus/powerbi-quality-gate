using System.Text;

namespace PowerBIQualityGate.Core.Inspection;

/// <summary>
/// Verified declaration coordinate in a TMDL source file. FilePath is relative
/// to the project containing the .pbip and always uses forward slashes.
/// </summary>
public sealed record TmdlSourceCoordinate(string FilePath, int Line);

/// <summary>
/// Read-only, fail-closed source index. TOM remains authoritative for model
/// semantics; this class only locates matching declarations in the source.
/// No DAX evaluation or TMDL deserialization happens here.
/// </summary>
public sealed class TmdlSourceLocationResolver
{
    private readonly Dictionary<(string Table, string Member), TmdlSourceCoordinate> _columns = new();
    private readonly Dictionary<(string Table, string Member), TmdlSourceCoordinate> _measures = new();
    private readonly Dictionary<string, TmdlSourceCoordinate> _relationships = new(StringComparer.Ordinal);
    private readonly HashSet<(string Table, string Member)> _ambiguousColumns = [];
    private readonly HashSet<(string Table, string Member)> _ambiguousMeasures = [];
    private readonly HashSet<string> _ambiguousRelationships = new(StringComparer.Ordinal);

    /// <param name="definitionDirectory">Full path to SemanticModel/definition.</param>
    /// <param name="projectDirectory">Full path to the folder containing the PBIP shortcut.</param>
    public TmdlSourceLocationResolver(string definitionDirectory, string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ReadTables(definitionDirectory, projectDirectory);
        ReadRelationships(definitionDirectory, projectDirectory);
    }

    /// <summary>
    /// Matches only known SM rule locations against *unique* TOM objects and
    /// unique source declarations. Unresolved or ambiguous results are null.
    /// </summary>
    public TmdlSourceCoordinate? Resolve(string ruleCode, string objectPath, TomModelSnapshot model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(objectPath))
        {
            return null;
        }

        if (ruleCode is "SM-001" or "SM-004")
        {
            var matches = model.Relationships
                .Where(relationship => string.Equals(objectPath,
                    $"{relationship.From} -> {relationship.To} (relationship: {relationship.Name})",
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            return matches.Length == 1 &&
                   !_ambiguousRelationships.Contains(matches[0].Name) &&
                   _relationships.TryGetValue(matches[0].Name, out var coordinate)
                ? coordinate
                : null;
        }

        if (ruleCode == "SM-003")
        {
            var matches = model.Tables
                .SelectMany(table => table.Columns.Select(column => (Table: table.Name, Member: column.Name)))
                .Where(item => string.Equals(objectPath, $"{item.Table}[{item.Member}]",
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            return matches.Length == 1 &&
                   !_ambiguousColumns.Contains(matches[0]) &&
                   _columns.TryGetValue(matches[0], out var coordinate)
                ? coordinate
                : null;
        }

        if (ruleCode == "SM-002")
        {
            var matches = model.Tables
                .SelectMany(table => table.Measures.Select(measure => (Table: table.Name, Member: measure.Name)))
                .Where(item => string.Equals(objectPath, $"{item.Table}[{item.Member}]",
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            return matches.Length == 1 &&
                   !_ambiguousMeasures.Contains(matches[0]) &&
                   _measures.TryGetValue(matches[0], out var coordinate)
                ? coordinate
                : null;
        }

        return null;
    }

    private void ReadTables(string definitionDirectory, string projectDirectory)
    {
        var tablesDirectory = Path.Combine(definitionDirectory, "tables");
        if (!Directory.Exists(tablesDirectory))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(tablesDirectory, "*.tmdl", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                var source = RelativeCoordinate(projectDirectory, file, 1);
                if (source is null || !TryReadLines(file, out var lines))
                {
                    continue;
                }

                var declarations = lines
                    .Select((line, index) => new { Line = line, Index = index })
                    .Where(item => Indentation(item.Line) == 0 &&
                                   TryReadName(item.Line, "table", out _))
                    .ToArray();
                if (declarations.Length != 1 ||
                    !TryReadName(declarations[0].Line, "table", out var tableName))
                {
                    continue;
                }

                var headerIndex = declarations[0].Index;
                var childIndent = lines.Skip(headerIndex + 1)
                    .Where(line => !string.IsNullOrWhiteSpace(line) && Indentation(line) > 0)
                    .Select(Indentation)
                    .DefaultIfEmpty(int.MaxValue)
                    .Min();

                for (var i = headerIndex + 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (Indentation(line) != childIndent)
                    {
                        continue;
                    }
                    var content = line.TrimStart(' ', '\t');
                    var coordinate = new TmdlSourceCoordinate(source.FilePath, i + 1);
                    if (TryReadName(content, "measure", out var measureName))
                    {
                        AddUnique(_measures, _ambiguousMeasures, (tableName, measureName), coordinate);
                    }
                    else if (TryReadName(content, "column", out var columnName) ||
                             TryReadName(content, "calculatedColumn", out columnName) ||
                             TryReadName(content, "calculatedTableColumn", out columnName))
                    {
                        AddUnique(_columns, _ambiguousColumns, (tableName, columnName), coordinate);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable source must not convert a completed audit into a failure.
        }
    }

    private void ReadRelationships(string definitionDirectory, string projectDirectory)
    {
        var file = Path.Combine(definitionDirectory, "relationships.tmdl");
        var source = RelativeCoordinate(projectDirectory, file, 1);
        if (source is null || !TryReadLines(file, out var lines))
        {
            return;
        }

        for (var i = 0; i < lines.Length; i++)
        {
            if (Indentation(lines[i]) == 0 && TryReadName(lines[i], "relationship", out var name))
            {
                AddUnique(_relationships, _ambiguousRelationships, name,
                    new TmdlSourceCoordinate(source.FilePath, i + 1));
            }
        }
    }

    private static void AddUnique<TKey>(Dictionary<TKey, TmdlSourceCoordinate> index,
        HashSet<TKey> ambiguous, TKey key, TmdlSourceCoordinate coordinate) where TKey : notnull
    {
        if (ambiguous.Contains(key))
        {
            return;
        }
        if (!index.TryAdd(key, coordinate))
        {
            index.Remove(key);
            ambiguous.Add(key);
        }
    }

    private static TmdlSourceCoordinate? RelativeCoordinate(string projectDirectory, string file, int line)
    {
        var relative = Path.GetRelativePath(projectDirectory, file);
        if (Path.IsPathRooted(relative) ||
            relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar })
                .Any(part => part == ".."))
        {
            return null;
        }
        return new TmdlSourceCoordinate(relative.Replace('\\', '/'), line);
    }

    private static bool TryReadLines(string file, out string[] lines)
    {
        lines = [];
        try
        {
            if (!File.Exists(file))
            {
                return false;
            }
            lines = File.ReadAllLines(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static int Indentation(string line)
    {
        var count = 0;
        while (count < line.Length && (line[count] == ' ' || line[count] == '\t'))
        {
            count++;
        }
        return count;
    }

    private static bool TryReadName(string line, string keyword, out string name)
    {
        name = string.Empty;
        if (!line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) ||
            line.Length <= keyword.Length || !char.IsWhiteSpace(line[keyword.Length]))
        {
            return false;
        }

        var remainder = line[keyword.Length..].TrimStart();
        if (remainder.Length == 0)
        {
            return false;
        }
        if (remainder[0] != '\'')
        {
            var end = remainder.IndexOfAny(new[] { ' ', '\t', '=', ':' });
            name = end < 0 ? remainder : remainder[..end];
            return name.Length > 0;
        }

        var value = new StringBuilder();
        for (var i = 1; i < remainder.Length; i++)
        {
            if (remainder[i] != '\'')
            {
                value.Append(remainder[i]);
                continue;
            }
            if (i + 1 < remainder.Length && remainder[i + 1] == '\'')
            {
                value.Append('\'');
                i++;
                continue;
            }
            var suffix = remainder[(i + 1)..];
            if (suffix.Length > 0 && !char.IsWhiteSpace(suffix[0]) && suffix[0] != '=' && suffix[0] != ':')
            {
                return false;
            }
            name = value.ToString();
            return name.Length > 0;
        }
        return false;
    }
}
