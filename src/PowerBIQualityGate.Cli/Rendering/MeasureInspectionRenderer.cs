using PowerBIQualityGate.Core.Inspection;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>
/// Privacy-safe aggregate-only measure inventory. No names, DAX expressions,
/// format string values, ANSI codes or terminal escape sequences are emitted.
/// </summary>
public sealed class MeasureInspectionRenderer
{
    private readonly TextWriter _writer;

    public MeasureInspectionRenderer(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void Render(string pbipPath, MeasureInspectionReport report)
    {
        ArgumentNullException.ThrowIfNull(pbipPath);
        ArgumentNullException.ThrowIfNull(report);

        var s = report.Summary;
        _writer.WriteLine("PBIQ  /  MEASURE METADATA INSPECTION");
        _writer.WriteLine($"Project: {Path.GetFileName(pbipPath)}");
        _writer.WriteLine($"Measures inspected: {s.Inspected}/{s.Total}  (visible {s.Visible}, hidden {s.Hidden})");
        _writer.WriteLine($"Format (visible): static {s.StaticFormats}  |  dynamic {s.DynamicFormats}  |  missing {s.MissingFormats}");
        _writer.WriteLine($"Type (visible): numeric {s.NumericTypes}  |  nonnumeric {s.NonNumericTypes}  |  unknown {s.UnknownTypes}  |  unsupported {s.UnsupportedTypes}");
        _writer.WriteLine($"Metadata notes: blank static {s.BlankStaticFormats}  |  blank dynamic expression {s.BlankDynamicExpressions}  |  unavailable dynamic expression {s.UnavailableDynamicExpressions}  |  both format kinds {s.BothFormats}");
        _writer.WriteLine($"Static DAX evidence (TOM unknown/unsupported): likely numeric {s.StaticDaxLikelyNumeric}  |  likely nonnumeric {s.StaticDaxLikelyNonNumeric}  |  unresolved {s.StaticDaxUnresolved}");
        _writer.WriteLine($"Static DAX review: missing format among likely numeric {s.StaticDaxLikelyNumericMissingFormat} (advisory only; not SM-002 warnings)");
        _writer.WriteLine("Scope: metadata presence plus advisory lexical DAX type evidence; no DAX execution or SM-002 verdict changes.");
    }
}
