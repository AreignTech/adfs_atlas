using System.Diagnostics;

namespace Atlas.Application.Observability;

/// <summary>Only static operation names and technical metadata belong in these spans.</summary>
public static class ApplicationTracing
{
    public static readonly ActivitySource Source = new("Atlas.Application");
}
