namespace Orange_Tendering_System.Services;

public sealed class ExecutiveModelException(string kind, string message, bool transient = false, int? httpStatus = null, Exception? inner = null)
    : Exception(message, inner)
{
    public string Kind { get; } = kind;
    public bool Transient { get; } = transient;
    public int? HttpStatus { get; } = httpStatus;
    public int Attempt { get; set; }
    public string? ResponseHash { get; set; }
}
public sealed record ExecutiveModelRetry(string Kind, int Attempt, int MaxAttempts, int? HttpStatus);
