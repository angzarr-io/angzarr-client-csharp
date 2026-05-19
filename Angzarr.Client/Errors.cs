namespace Angzarr.Client;

/// <summary>
/// Base exception for all Angzarr client errors.
///
/// <para>Audit finding #59 (structural error model). Every error carries:</para>
/// <list type="bullet">
///   <item>A static <see cref="Exception.Message"/> — the same exact string for
///   the same predicate failure across all call sites, suitable for log
///   greppability and cross-language equality.</item>
///   <item>A stable <see cref="Code"/> identifier (SCREAMING_SNAKE) — programmatic
///   dispatch and cucumber assertions key off this.</item>
///   <item>Structured <see cref="Details"/> — runtime context that varies per
///   call site.</item>
/// </list>
///
/// <para>Callers MUST NOT interpolate runtime values into the message; that
/// defeats the static-string contract. Put runtime values in
/// <see cref="Details"/>.</para>
/// </summary>
public class ClientError : Exception
{
    /// <summary>
    /// Stable SCREAMING_SNAKE identifier (audit #59). See
    /// <see cref="ErrorCodes"/> for the canonical list.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Structured runtime context (audit #59). Cucumber assertions read from
    /// <see cref="Details"/> rather than parsing the message string.
    /// </summary>
    public IReadOnlyDictionary<string, string> Details { get; }

    public ClientError(string message, string code = "", IDictionary<string, string>? details = null)
        : base(message)
    {
        Code = code;
        Details = details != null
            ? new Dictionary<string, string>(details)
            : new Dictionary<string, string>();
    }

    public ClientError(
        string message,
        Exception inner,
        string code = "",
        IDictionary<string, string>? details = null
    )
        : base(message, inner)
    {
        Code = code;
        Details = details != null
            ? new Dictionary<string, string>(details)
            : new Dictionary<string, string>();
    }

    /// <summary>
    /// Returns true if this is a "not found" error.
    /// </summary>
    public virtual bool IsNotFound() => false;

    /// <summary>
    /// Returns true if this is a "precondition failed" error.
    /// </summary>
    public virtual bool IsPreconditionFailed() => false;

    /// <summary>
    /// Returns true if this is an "invalid argument" error.
    /// </summary>
    public virtual bool IsInvalidArgument() => false;

    /// <summary>
    /// Returns true if this is a connection or transport error.
    /// </summary>
    public virtual bool IsConnectionError() => false;
}

/// <summary>
/// Thrown when a command is rejected by business logic.
///
/// <para>Status codes and retry semantics mirror Python/Rust:</para>
/// <list type="bullet">
///   <item><c>FAILED_PRECONDITION</c>: state-based rejection; retryable after refreshing state.</item>
///   <item><c>INVALID_ARGUMENT</c>: bad input; not retryable.</item>
///   <item><c>NOT_FOUND</c>: aggregate does not exist; not retryable.</item>
/// </list>
///
/// <para>Audit finding #59: callers pass a static <c>message</c>, a SCREAMING_SNAKE
/// <c>code</c>, and structured <c>details</c>. The factory methods
/// (<see cref="PreconditionFailed(string,string,IDictionary{string,string})"/>,
/// <see cref="InvalidArgument(string,string,IDictionary{string,string})"/>,
/// <see cref="NotFound(string,string,IDictionary{string,string})"/>) bind the
/// appropriate <see cref="StatusCode"/>.</para>
/// </summary>
public class CommandRejectedError : ClientError
{
    /// <summary>
    /// gRPC-style status (<c>FAILED_PRECONDITION</c> / <c>INVALID_ARGUMENT</c>
    /// / <c>NOT_FOUND</c>). Mirrors Python's <c>status_code</c> attribute.
    /// </summary>
    public string StatusCode { get; }

    public CommandRejectedError(string message)
        : base(message, code: "")
    {
        StatusCode = "FAILED_PRECONDITION";
    }

    public CommandRejectedError(string message, string statusCode)
        : base(message, code: "")
    {
        StatusCode = statusCode;
    }

    public CommandRejectedError(
        string message,
        string statusCode,
        string code,
        IDictionary<string, string>? details
    )
        : base(message, code: code, details: details)
    {
        StatusCode = statusCode;
    }

    public override bool IsPreconditionFailed() => StatusCode == "FAILED_PRECONDITION";

    public override bool IsInvalidArgument() => StatusCode == "INVALID_ARGUMENT";

    public override bool IsNotFound() => StatusCode == "NOT_FOUND";

    /// <summary>
    /// Create a precondition failed error (legacy single-arg overload).
    /// </summary>
    public static CommandRejectedError PreconditionFailed(string message)
    {
        return new CommandRejectedError(message, "FAILED_PRECONDITION");
    }

    /// <summary>
    /// Create a precondition failed error with structured code/details.
    ///
    /// <para>Audit #59 — preferred form. <paramref name="code"/> should come from
    /// <see cref="ErrorCodes"/>; <paramref name="message"/> from
    /// <see cref="ErrorMessages"/>; <paramref name="details"/> keys from
    /// <see cref="ErrorKeys"/>.</para>
    /// </summary>
    public static CommandRejectedError PreconditionFailed(
        string code,
        string message,
        IDictionary<string, string>? details = null
    )
    {
        return new CommandRejectedError(message, "FAILED_PRECONDITION", code, details);
    }

    /// <summary>
    /// Create an invalid argument error (legacy single-arg overload).
    /// </summary>
    public static CommandRejectedError InvalidArgument(string message)
    {
        return new CommandRejectedError(message, "INVALID_ARGUMENT");
    }

    /// <summary>
    /// Create an invalid argument error with structured code/details (audit #59).
    /// </summary>
    public static CommandRejectedError InvalidArgument(
        string code,
        string message,
        IDictionary<string, string>? details = null
    )
    {
        return new CommandRejectedError(message, "INVALID_ARGUMENT", code, details);
    }

    /// <summary>
    /// Create a not found error (legacy single-arg overload).
    /// </summary>
    public static CommandRejectedError NotFound(string message)
    {
        return new CommandRejectedError(message, "NOT_FOUND");
    }

    /// <summary>
    /// Create a not found error with structured code/details (audit #59).
    /// </summary>
    public static CommandRejectedError NotFound(
        string code,
        string message,
        IDictionary<string, string>? details = null
    )
    {
        return new CommandRejectedError(message, "NOT_FOUND", code, details);
    }
}

/// <summary>
/// Thrown when a gRPC call fails.
/// </summary>
public class GrpcError : ClientError
{
    public Grpc.Core.StatusCode StatusCode { get; }

    public GrpcError(string message, Grpc.Core.StatusCode statusCode)
        : base(message, code: ErrorCodes.GrpcError)
    {
        StatusCode = statusCode;
    }

    public override bool IsNotFound() => StatusCode == Grpc.Core.StatusCode.NotFound;

    public override bool IsPreconditionFailed() =>
        StatusCode == Grpc.Core.StatusCode.FailedPrecondition;

    public override bool IsInvalidArgument() => StatusCode == Grpc.Core.StatusCode.InvalidArgument;

    public override bool IsConnectionError() => StatusCode == Grpc.Core.StatusCode.Unavailable;
}

/// <summary>
/// Thrown when connection to the server fails.
/// </summary>
public class ConnectionError : ClientError
{
    public ConnectionError(string message)
        : base(message, code: ErrorCodes.ConnectionFailed) { }

    public ConnectionError(string message, Exception inner)
        : base(message, inner, code: ErrorCodes.ConnectionFailed) { }

    public ConnectionError(string message, string code, IDictionary<string, string>? details = null)
        : base(message, code: code, details: details) { }

    public override bool IsConnectionError() => true;
}

/// <summary>
/// Thrown when transport-level errors occur.
/// </summary>
public class TransportError : ClientError
{
    public TransportError(string message)
        : base(message, code: ErrorCodes.TransportError) { }

    public TransportError(string message, Exception inner)
        : base(message, inner, code: ErrorCodes.TransportError) { }

    public TransportError(string message, string code, IDictionary<string, string>? details = null)
        : base(message, code: code, details: details) { }

    public override bool IsConnectionError() => true;
}

/// <summary>
/// Thrown when an invalid argument is provided.
/// </summary>
public class InvalidArgumentError : ClientError
{
    // Spec LOW-1.13: vestigial "INVALID_ARGUMENT" default removed.
    // Callers should pass a domain-specific code from ErrorCodes;
    // an empty code is preferable to a stamped non-inventory token.
    public InvalidArgumentError(string message)
        : base(message, code: "") { }

    public InvalidArgumentError(string message, string code, IDictionary<string, string>? details = null)
        : base(message, code: code, details: details) { }

    public override bool IsInvalidArgument() => true;
}

/// <summary>
/// Thrown when a timestamp cannot be parsed.
/// </summary>
public class InvalidTimestampError : ClientError
{
    public InvalidTimestampError(string message)
        : base(message, code: "INVALID_TIMESTAMP") { }
}
