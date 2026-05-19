namespace Angzarr.Client;

/// <summary>
/// Validation helper methods that throw <see cref="CommandRejectedError"/> on failure.
///
/// <para>Audit finding #59 (structural error model). Errors carry a SCREAMING_SNAKE
/// <see cref="ClientError.Code"/> (from <see cref="ErrorCodes"/>), a static
/// <see cref="System.Exception.Message"/> (from <see cref="ErrorMessages"/>), and
/// structured <see cref="ClientError.Details"/> (keyed by <see cref="ErrorKeys"/>).</para>
///
/// <para>Audit finding #60: <c>RequireExists</c>/<c>RequireNotExists</c> take a
/// caller-supplied predicate boolean (e.g., <c>state != null</c>) so the
/// callsite is explicit about what "exists" means.</para>
///
/// <para>Audit finding #18 (Rust 2023261): <c>RequireExists</c> raises NOT_FOUND
/// — refetching events cannot change the outcome.</para>
/// </summary>
public static class Validation
{
    private static IDictionary<string, string> ContextDetails(string context) =>
        new Dictionary<string, string> { [ErrorKeys.Context] = context };

    private static IDictionary<string, string> FieldDetails(string field) =>
        new Dictionary<string, string> { [ErrorKeys.Field] = field };

    /// <summary>
    /// Require that an aggregate exists (caller-supplied predicate boolean).
    /// Raises NOT_FOUND on failure (audit #18).
    /// </summary>
    public static void RequireExists(bool exists, string context = "<entity>")
    {
        if (!exists)
            throw CommandRejectedError.NotFound(
                ErrorCodes.EntityNotFound,
                ErrorMessages.EntityNotFound,
                ContextDetails(context)
            );
    }

    /// <summary>
    /// Require that an aggregate does NOT exist (caller-supplied predicate).
    /// Raises FAILED_PRECONDITION on failure.
    /// </summary>
    public static void RequireNotExists(bool exists, string context = "<entity>")
    {
        if (exists)
            throw CommandRejectedError.PreconditionFailed(
                ErrorCodes.EntityAlreadyExists,
                ErrorMessages.EntityAlreadyExists,
                ContextDetails(context)
            );
    }

    /// <summary>
    /// Require that a value is positive (greater than zero).
    /// </summary>
    public static void RequirePositive(decimal value, string fieldName = "value")
    {
        if (value <= 0)
            throw CommandRejectedError.InvalidArgument(
                ErrorCodes.ValueNotPositive,
                ErrorMessages.ValueNotPositive,
                FieldDetails(fieldName)
            );
    }

    /// <inheritdoc cref="RequirePositive(decimal,string)"/>
    public static void RequirePositive(int value, string fieldName = "value") =>
        RequirePositive((decimal)value, fieldName);

    /// <inheritdoc cref="RequirePositive(decimal,string)"/>
    public static void RequirePositive(long value, string fieldName = "value") =>
        RequirePositive((decimal)value, fieldName);

    /// <summary>
    /// Require that a value is non-negative (zero or greater).
    /// </summary>
    public static void RequireNonNegative(decimal value, string fieldName = "value")
    {
        if (value < 0)
            throw CommandRejectedError.InvalidArgument(
                ErrorCodes.ValueNotNonNegative,
                ErrorMessages.ValueNotNonNegative,
                FieldDetails(fieldName)
            );
    }

    /// <inheritdoc cref="RequireNonNegative(decimal,string)"/>
    public static void RequireNonNegative(int value, string fieldName = "value") =>
        RequireNonNegative((decimal)value, fieldName);

    /// <inheritdoc cref="RequireNonNegative(decimal,string)"/>
    public static void RequireNonNegative(long value, string fieldName = "value") =>
        RequireNonNegative((decimal)value, fieldName);

    /// <summary>
    /// Require that a string is not empty.
    /// </summary>
    public static void RequireNotEmpty(string? value, string fieldName = "value")
    {
        if (string.IsNullOrEmpty(value))
            throw CommandRejectedError.InvalidArgument(
                ErrorCodes.ValueEmpty,
                ErrorMessages.ValueEmpty,
                FieldDetails(fieldName)
            );
    }

    /// <summary>
    /// Require that a collection is not empty.
    /// </summary>
    public static void RequireNotEmpty<T>(
        IEnumerable<T>? collection,
        string fieldName = "collection"
    )
    {
        if (collection == null || !collection.Any())
            throw CommandRejectedError.InvalidArgument(
                ErrorCodes.CollectionEmpty,
                ErrorMessages.CollectionEmpty,
                FieldDetails(fieldName)
            );
    }

    /// <summary>
    /// Require that a status matches an expected value.
    /// </summary>
    public static void RequireStatus<T>(T actual, T expected, string context = "status")
        where T : struct, Enum
    {
        if (!actual.Equals(expected))
            throw CommandRejectedError.PreconditionFailed(
                ErrorCodes.StatusMismatch,
                ErrorMessages.StatusMismatch,
                ContextDetails(context)
            );
    }

    /// <summary>
    /// Require that a status does not match a forbidden value.
    /// </summary>
    public static void RequireStatusNot<T>(T actual, T forbidden, string context = "status")
        where T : struct, Enum
    {
        if (actual.Equals(forbidden))
            throw CommandRejectedError.PreconditionFailed(
                ErrorCodes.StatusForbidden,
                ErrorMessages.StatusForbidden,
                ContextDetails(context)
            );
    }
}
