namespace PowerBIQualityGate.Core.Validation;

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Information
}

public sealed record ProjectDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    string Path);
