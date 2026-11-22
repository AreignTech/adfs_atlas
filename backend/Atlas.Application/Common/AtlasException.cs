namespace Atlas.Application.Common;

/// <summary>
/// Base class for expected, user-facing failures. The API maps these to
/// RFC 7807 problem responses without leaking stack traces.
/// </summary>
public class AtlasException : Exception
{
    public AtlasException(string message, int statusCode, string title)
        : base(message)
    {
        StatusCode = statusCode;
        Title = title;
    }

    public int StatusCode { get; }
    public string Title { get; }
}

public sealed class ValidationException : AtlasException
{
    public ValidationException(string message)
        : base(message, 400, "Validation failed.")
    {
    }
}

public sealed class UnauthorizedException : AtlasException
{
    public UnauthorizedException(string message = "Authentication required.")
        : base(message, 401, "Authentication required.")
    {
    }
}

public sealed class ForbiddenException : AtlasException
{
    public ForbiddenException(string message = "You do not have permission to perform this action.")
        : base(message, 403, "Forbidden.")
    {
    }
}

public sealed class NotFoundException : AtlasException
{
    public NotFoundException(string message)
        : base(message, 404, "Not found.")
    {
    }
}

public sealed class ConflictException : AtlasException
{
    public ConflictException(string message)
        : base(message, 409, "Conflict.")
    {
    }
}
