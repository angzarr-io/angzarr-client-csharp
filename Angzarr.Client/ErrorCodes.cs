namespace Angzarr.Client;

/// <summary>
/// SCREAMING_SNAKE event-type identifiers — the value of <c>Code</c> on every
/// error this client constructs.
///
/// <para>Mirrors the Rust <c>error_codes::codes</c> and Python
/// <c>error_codes.codes</c> — same constant names, same string values.</para>
///
/// <para>Audit finding #59 (structural error model). Errors carry a static
/// <c>Message</c>, a stable <c>Code</c> (these constants), and structured
/// <c>Details</c> (runtime context).</para>
/// </summary>
public static class ErrorCodes
{
    // ---------------------------------------------------------------- Validation
    public const string ValueNotPositive = "VALUE_NOT_POSITIVE";
    public const string ValueNotNonNegative = "VALUE_NOT_NON_NEGATIVE";
    public const string ValueEmpty = "VALUE_EMPTY";
    public const string CollectionEmpty = "COLLECTION_EMPTY";
    public const string EntityNotFound = "ENTITY_NOT_FOUND";
    public const string EntityAlreadyExists = "ENTITY_ALREADY_EXISTS";
    public const string StatusMismatch = "STATUS_MISMATCH";
    public const string StatusForbidden = "STATUS_FORBIDDEN";

    // ---------------------------------------------------------------- Builder
    public const string CommandTypeUrlMissing = "COMMAND_TYPE_URL_MISSING";
    public const string CommandPayloadMissing = "COMMAND_PAYLOAD_MISSING";
    public const string CommandSequenceMissing = "COMMAND_SEQUENCE_MISSING";

    // ---------------------------------------------------------------- Conversion
    public const string AnyTypeMismatch = "ANY_TYPE_MISMATCH";
    public const string AnyDecodeFailed = "ANY_DECODE_FAILED";
    public const string ProtoUuidInvalid = "PROTO_UUID_INVALID";
    public const string TimestampParseFailed = "TIMESTAMP_PARSE_FAILED";

    // ---------------------------------------------------------------- Transport
    public const string EndpointParseFailed = "ENDPOINT_PARSE_FAILED";
    public const string EndpointInvalidUri = "ENDPOINT_INVALID_URI";
    public const string ConnectionFailed = "CONNECTION_FAILED";
    public const string ConnectionFailedMaxRetries = "CONNECTION_FAILED_MAX_RETRIES";
    public const string TransportError = "TRANSPORT_ERROR";
    public const string GrpcError = "GRPC_ERROR";

    // Audit #40 — loud-fail on bad ANGZARR_MODE / ANGZARR_CH_PORT.
    public const string InvalidTransportMode = "INVALID_TRANSPORT_MODE";
    public const string InvalidPort = "INVALID_PORT";

    // ---------------------------------------------------------------- Dispatch — common
    public const string HandlerWrongResponseKind = "HANDLER_WRONG_RESPONSE_KIND";
    public const string HandlerWrongRequestKind = "HANDLER_WRONG_REQUEST_KIND";
    public const string NoHandlerRegistered = "NO_HANDLER_REGISTERED";
    public const string MissingCommandBook = "MISSING_COMMAND_BOOK";
    public const string MissingCommandPage = "MISSING_COMMAND_PAGE";
    public const string MissingCommandPayload = "MISSING_COMMAND_PAYLOAD";
    public const string NotificationDecodeFailed = "NOTIFICATION_DECODE_FAILED";
    public const string RejectionNotificationDecodeFailed = "REJECTION_NOTIFICATION_DECODE_FAILED";

    // ---------------------------------------------------------------- Dispatch — saga
    public const string MissingSagaSource = "MISSING_SAGA_SOURCE";
    public const string EmptySagaSource = "EMPTY_SAGA_SOURCE";
    public const string MissingSagaEventPayload = "MISSING_SAGA_EVENT_PAYLOAD";
    public const string SagaInvalidTypeUrl = "SAGA_INVALID_TYPE_URL";
    public const string SagaHandlerUnsupportedReturnType = "SAGA_HANDLER_UNSUPPORTED_RETURN_TYPE";

    // ---------------------------------------------------------------- Dispatch — process manager
    public const string MissingPmTrigger = "MISSING_PM_TRIGGER";
    public const string EmptyPmTrigger = "EMPTY_PM_TRIGGER";
    public const string MissingPmEventPayload = "MISSING_PM_EVENT_PAYLOAD";
    public const string PmInvalidTypeUrl = "PM_INVALID_TYPE_URL";
    public const string PmHandlerWrongReturnType = "PM_HANDLER_WRONG_RETURN_TYPE";

    // ---------------------------------------------------------------- Dispatch — upcaster
    public const string UpcasterWrongResponseKind = "UPCASTER_WRONG_RESPONSE_KIND";

    // ---------------------------------------------------------------- Build-time validation
    public const string HandlerFieldEmptyString = "HANDLER_FIELD_EMPTY_STRING";
    public const string HandlerFieldEmptyList = "HANDLER_FIELD_EMPTY_LIST";
    public const string HandlerStateNotType = "HANDLER_STATE_NOT_TYPE";
    public const string HandlerUnknownKind = "HANDLER_UNKNOWN_KIND";
    public const string RouterNoHandlers = "ROUTER_NO_HANDLERS";

    // Audit #62: a Router was built with two CommandHandler factories
    // claiming the same (domain, type_url) pair.
    public const string DuplicateCommandHandler = "DUPLICATE_COMMAND_HANDLER";

    // Audit #72: a Router was built with handlers of different kinds.
    public const string MixedHandlerKinds = "MIXED_HANDLER_KINDS";

    // ---------------------------------------------------------------- Saga / PM destinations
    // Audit #64: saga/PM tried to stamp a command for a domain not in
    // the request's destination_sequences map.
    public const string MissingDestinationSequence = "MISSING_DESTINATION_SEQUENCE";

    // ---------------------------------------------------------------- Examples — Table
    // PR #12 design decision (2026-05-18): ChangeSeats reject code when
    // current_seat == requested_seat. Handler-side validation; emitted
    // before any seat-lookup logic. Mirrored in Py/Rs/Go/Ja/Cpp error_codes.
    public const string SeatsIdentical = "SEATS_IDENTICAL";
}

/// <summary>
/// Static human-readable messages — the value of <c>Message</c> on every
/// error this client constructs. Constant names match the corresponding
/// <see cref="ErrorCodes"/> constant.
///
/// <para>Audit #59: callers pass a static message (no interpolation); runtime
/// values ride in the <c>Details</c> dictionary.</para>
/// </summary>
public static class ErrorMessages
{
    // Validation
    public const string ValueNotPositive = "value must be positive";
    public const string ValueNotNonNegative = "value must be non-negative";
    public const string ValueEmpty = "value must not be empty";
    public const string CollectionEmpty = "collection must not be empty";
    public const string EntityNotFound = "entity not found";
    public const string EntityAlreadyExists = "entity already exists";
    public const string StatusMismatch = "status does not match expected";
    public const string StatusForbidden = "status is the forbidden value";

    // Builder
    public const string CommandTypeUrlMissing = "command type_url not set";
    public const string CommandPayloadMissing = "command payload not set";

    // Spec LOW-1.11: canonical snake_case method-name hint matching Py/Rs/Ja/Cpp.
    // C# previously had "(call WithSequence)" which broke cross-language byte-equal
    // assertions; the method-name in C# code is still WithSequence (idiomatic) but
    // the surface error text is the cross-language constant.
    public const string CommandSequenceMissing = "sequence not set (call with_sequence)";

    // Spec LOW-3.14: stamp when WithSequence receives an out-of-range value.
    public const string CommandSequenceOutOfRange = "sequence out of uint32 range";

    // Conversion
    public const string AnyTypeMismatch = "Any type_url does not match expected message type";
    public const string AnyDecodeFailed = "failed to decode Any payload into expected message type";
    public const string ProtoUuidInvalid = "proto UUID bytes are not a valid 16-byte UUID";
    public const string TimestampParseFailed = "failed to parse RFC3339 timestamp";

    // Transport
    public const string EndpointParseFailed = "failed to parse fallback endpoint";
    public const string EndpointInvalidUri = "endpoint URI is invalid";
    public const string ConnectionFailed = "connection failed";
    public const string ConnectionFailedMaxRetries = "connection failed after max retries";
    public const string TransportError = "transport error";
    public const string GrpcError = "grpc error";
    public const string InvalidTransportMode = "invalid transport mode env value";
    public const string InvalidPort = "invalid port env value";

    // Dispatch — common
    public const string HandlerWrongResponseKind = "handler returned wrong response kind";
    public const string HandlerWrongRequestKind = "handler dispatched with wrong request kind";
    public const string NoHandlerRegistered = "no handler registered for the given (domain, type_url)";
    public const string MissingCommandBook = "missing command book";
    public const string MissingCommandPage = "missing command page";
    public const string MissingCommandPayload = "missing command payload";
    public const string NotificationDecodeFailed = "failed to decode Notification payload";
    public const string RejectionNotificationDecodeFailed = "failed to decode RejectionNotification payload";

    // Dispatch — saga
    public const string MissingSagaSource = "missing saga source";
    public const string EmptySagaSource = "empty saga source";
    public const string MissingSagaEventPayload = "missing saga event payload";
    public const string SagaInvalidTypeUrl = "saga invalid type_url";
    public const string SagaHandlerUnsupportedReturnType = "saga handler unsupported return type";

    // Dispatch — process manager
    public const string MissingPmTrigger = "missing process manager trigger";
    public const string EmptyPmTrigger = "empty process manager trigger";
    public const string MissingPmEventPayload = "missing process manager event payload";
    public const string PmInvalidTypeUrl = "process manager invalid type_url";
    public const string PmHandlerWrongReturnType = "process manager handler wrong return type";

    // Dispatch — upcaster
    public const string UpcasterWrongResponseKind = "upcaster wrong response kind";

    // Build-time validation
    public const string HandlerFieldEmptyString = "handler annotation field is empty string";
    public const string HandlerFieldEmptyList = "handler annotation field is empty list";
    public const string HandlerStateNotType = "handler state field is not a type";
    public const string HandlerUnknownKind = "handler class has no recognized kind decoration";
    public const string RouterNoHandlers = "no handlers registered on Router";
    public const string DuplicateCommandHandler =
        "duplicate command handler registration for (domain, type_url)";
    public const string MixedHandlerKinds =
        "cannot mix handler kinds in one Router — all handlers must share a kind";

    // Saga / PM destinations
    public const string MissingDestinationSequence = "no sequence for destination domain";

    // Examples — Table (PR #12 SEATS_IDENTICAL; canonical text from Py/Rs)
    public const string SeatsIdentical = "current_seat and requested_seat are identical";
}

/// <summary>
/// Detail-key constants for the <c>Details</c> dictionary on errors.
/// Mirrors Rust's <c>error_codes::keys</c> and Python's <c>error_codes.keys</c>.
/// </summary>
public static class ErrorKeys
{
    public const string Field = "field";
    public const string Domain = "domain";
    public const string TypeUrl = "type_url";
    public const string Endpoint = "endpoint";
    public const string Cause = "cause";
    public const string Context = "context";
    public const string Input = "input";
    public const string EnvVar = "env_var";
    public const string ActualReturnType = "actual_return_type";

    // Spec MED-1.6 — full key inventory parity with Py/Rs/Ja/Cpp.
    public const string Expected = "expected";
    public const string Actual = "actual";
    public const string ExpectedKind = "expected_kind";
    public const string RouterName = "router_name";
    public const string HandlerClass = "handler_class";
    public const string HandlerKind = "handler_kind";
    public const string OtherKind = "other_kind";
}
