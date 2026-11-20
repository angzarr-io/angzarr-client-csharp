namespace Angzarr.Client;

/// <summary>
/// Static helper methods for compensation flow handling.
///
/// <para>Provides convenient factory methods for creating compensation responses
/// in aggregates and process managers when handling saga/PM rejections.</para>
///
/// <example>
/// <code>
/// public Angzarr.BusinessResponse HandleRejection(Angzarr.Notification notification)
/// {
///     var ctx = CompensationContext.FromNotification(notification);
///
///     // Option 1: Emit compensation events
///     if (ctx.IssuerName == "saga-order-fulfillment")
///     {
///         var cancelEvent = new OrderCancelled { Reason = ctx.RejectionReason };
///         return Compensation.EmitCompensationEvents(NewEventBook(cancelEvent));
///     }
///
///     // Option 2: Delegate to framework
///     return Compensation.DelegateToFramework(
///         $"No custom compensation for {ctx.IssuerName}");
/// }
/// </code>
/// </example>
/// </summary>
public static class Compensation
{
    // --- Aggregate helpers ---

    /// <summary>
    /// Create a response that delegates compensation to the framework.
    ///
    /// <para>Use when the aggregate doesn't have custom compensation logic for a saga.
    /// The framework will emit a SagaCompensationFailed event to the fallback domain.</para>
    /// </summary>
    /// <param name="reason">Human-readable explanation for the delegation.</param>
    /// <returns>BusinessResponse with revocation flags.</returns>
    public static Angzarr.BusinessResponse DelegateToFramework(string reason)
    {
        return DelegateToFramework(reason, emitSystemEvent: true, sendToDeadLetter: false, escalate: false, abort: false);
    }

    /// <summary>
    /// Create a response that delegates compensation to the framework with custom options.
    /// </summary>
    /// <param name="reason">Human-readable explanation for the delegation.</param>
    /// <param name="emitSystemEvent">Emit SagaCompensationFailed to fallback domain.</param>
    /// <param name="sendToDeadLetter">Move failed event to dead letter queue.</param>
    /// <param name="escalate">Mark for operator intervention.</param>
    /// <param name="abort">Stop the saga entirely without retry.</param>
    /// <returns>BusinessResponse with revocation flags.</returns>
    public static Angzarr.BusinessResponse DelegateToFramework(
        string reason, bool emitSystemEvent, bool sendToDeadLetter, bool escalate, bool abort)
    {
        return new Angzarr.BusinessResponse
        {
            Revocation = new Angzarr.RevocationResponse
            {
                EmitSystemRevocation = emitSystemEvent,
                SendToDeadLetterQueue = sendToDeadLetter,
                Escalate = escalate,
                Abort = abort,
                Reason = reason,
            },
        };
    }

    /// <summary>
    /// Create a response containing compensation events.
    ///
    /// <para>Use when the aggregate emits events to record compensation.
    /// The framework will persist these events and NOT emit a system event.</para>
    /// </summary>
    /// <param name="eventBook">EventBook containing compensation events.</param>
    /// <returns>BusinessResponse with events.</returns>
    public static Angzarr.BusinessResponse EmitCompensationEvents(Angzarr.EventBook eventBook)
    {
        return new Angzarr.BusinessResponse
        {
            Events = eventBook,
        };
    }

    // --- Process Manager helpers ---

    /// <summary>
    /// Create a PM response that delegates compensation to the framework.
    ///
    /// <para>Use when the PM doesn't have custom compensation logic.</para>
    /// </summary>
    /// <param name="reason">Human-readable explanation for the delegation.</param>
    /// <returns>PMRevocationResponse with no PM events, delegating to framework.</returns>
    public static PMRevocationResponse PmDelegateToFramework(string reason)
    {
        return PmDelegateToFramework(reason, emitSystemEvent: true);
    }

    /// <summary>
    /// Create a PM response that delegates compensation to the framework.
    /// </summary>
    /// <param name="reason">Human-readable explanation for the delegation.</param>
    /// <param name="emitSystemEvent">Emit SagaCompensationFailed to fallback domain.</param>
    /// <returns>PMRevocationResponse with no PM events, delegating to framework.</returns>
    public static PMRevocationResponse PmDelegateToFramework(string reason, bool emitSystemEvent)
    {
        return new PMRevocationResponse
        {
            ProcessEvents = null,
            Revocation = new Angzarr.RevocationResponse
            {
                EmitSystemRevocation = emitSystemEvent,
                Reason = reason,
            },
        };
    }

    /// <summary>
    /// Create a PM response containing compensation events.
    ///
    /// <para>Use when the PM emits events to record the compensation in its state.</para>
    /// </summary>
    /// <param name="processEvents">EventBook containing PM compensation events.</param>
    /// <returns>RejectionHandlerResponse with events.</returns>
    public static RejectionHandlerResponse PmEmitCompensationEvents(Angzarr.EventBook processEvents)
    {
        return new RejectionHandlerResponse
        {
            Events = processEvents,
        };
    }

    /// <summary>
    /// Create a PM response with compensation events and system event emission.
    /// </summary>
    /// <param name="processEvents">EventBook containing PM compensation events.</param>
    /// <param name="reason">Reason for the system event.</param>
    /// <returns>RejectionHandlerResponse with events.</returns>
    public static RejectionHandlerResponse PmEmitCompensationEventsWithSystemEvent(
        Angzarr.EventBook processEvents, string reason)
    {
        return new RejectionHandlerResponse
        {
            Events = processEvents,
        };
    }
}

/// <summary>
/// Holds PM compensation results - process events and framework action flags.
/// </summary>
public class PMRevocationResponse
{
    /// <summary>
    /// PM events to persist (may be null).
    /// </summary>
    public Angzarr.EventBook? ProcessEvents { get; set; }

    /// <summary>
    /// Framework action flags for revocation handling.
    /// </summary>
    public Angzarr.RevocationResponse Revocation { get; set; } = new();
}
