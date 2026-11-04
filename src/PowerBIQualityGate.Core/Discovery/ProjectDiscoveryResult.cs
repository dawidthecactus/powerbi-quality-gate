namespace PowerBIQualityGate.Core.Discovery;

public enum DiscoveryStatus
{
    Found,
    InvalidPath,
    NotFound,
    MultipleProjects,
    AccessDenied
}

public sealed record ProjectDiscoveryResult(
    DiscoveryStatus Status,
    string? PbipPath,
    string Message)
{
    public bool IsSuccess => Status == DiscoveryStatus.Found;
}