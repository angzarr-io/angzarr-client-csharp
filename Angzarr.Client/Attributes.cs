namespace Angzarr.Client;

/// <summary>
/// Marks a method as a handler for the specified message type.
/// For aggregates: handles commands, returns events.
/// For sagas/PMs: handles events, returns commands.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class HandlesAttribute : Attribute
{
    public Type EventType { get; }
    public string? InputDomain { get; set; }
    public string? OutputDomain { get; set; }

    public HandlesAttribute(Type eventType)
    {
        EventType = eventType;
    }
}

/// <summary>
/// Marks a method as an event applier for the specified event type.
/// The method should mutate state in place.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class AppliesAttribute : Attribute
{
    public Type EventType { get; }

    public AppliesAttribute(Type eventType)
    {
        EventType = eventType;
    }
}

/// <summary>
/// Marks a method as a projector event handler.
/// The method should return a Projection.
///
/// <para>Spec MED-4.12: <see cref="ProjectsAttribute"/> is being phased out
/// in favor of the cross-language <see cref="HandlesAttribute"/>. The two
/// behave identically for projector dispatch; new code should use
/// <c>[Handles(typeof(...))]</c>.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[Obsolete("Use [Handles(typeof(...))] for cross-language uniformity per spec MED-4.12. [Projects] remains as a backward-compatibility alias.")]
public class ProjectsAttribute : Attribute
{
    public Type EventType { get; }

    public ProjectsAttribute(Type eventType)
    {
        EventType = eventType;
    }
}

/// <summary>
/// Marks a method as a rejection handler for compensation.
/// Called when a command issued by this component is rejected.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class RejectedAttribute : Attribute
{
    public string Domain { get; }
    public string Command { get; }

    public RejectedAttribute(string domain, string command)
    {
        Domain = domain;
        Command = command;
    }
}

/// <summary>
/// Marks a method as an upcaster for event version transformation.
/// The method should return the new event version.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class UpcastsAttribute : Attribute
{
    public Type FromType { get; }
    public Type ToType { get; }

    public UpcastsAttribute(Type fromType, Type toType)
    {
        FromType = fromType;
        ToType = toType;
    }
}

// ============================================================================
// Spec HIGH-4.2 — class-level kind attributes
//
// Mirror the Py/Rs/Ja decorator / annotation surface so a unified Router
// builder (see UnifiedRouter) can inspect a class and discover its kind +
// metadata at registration time. Without class-level decoration, the
// cross-language Router.build() validation invariants (audit #72) cannot
// be enforced in C#.
// ============================================================================

/// <summary>
/// Declares a class as an aggregate (command handler). The aggregate
/// owns commands for the named <see cref="Domain"/> and emits events.
/// Spec HIGH-4.2.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class AggregateAttribute : Attribute
{
    public string Domain { get; }
    public Type? State { get; }

    public AggregateAttribute(string domain, Type? state = null)
    {
        Domain = domain;
        State = state;
    }
}

/// <summary>
/// Declares a class as a saga (stateless event-to-command translator
/// between two domains). Spec HIGH-4.2.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class SagaAttribute : Attribute
{
    public string Name { get; }
    public string Source { get; }
    public string Target { get; }
    public bool Sync { get; }

    public SagaAttribute(string name, string source, string target, bool sync = false)
    {
        Name = name;
        Source = source;
        Target = target;
        Sync = sync;
    }
}

/// <summary>
/// Declares a class as a process manager (multi-domain event correlator
/// with state). Spec HIGH-4.2.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ProcessManagerAttribute : Attribute
{
    public string Name { get; }
    public string PmDomain { get; }
    public string[] Sources { get; }
    public string[] Targets { get; }
    public string[] SyncTargets { get; }
    public Type? State { get; }

    public ProcessManagerAttribute(
        string name,
        string pmDomain,
        string[] sources,
        string[] targets,
        string[]? syncTargets = null,
        Type? state = null)
    {
        Name = name;
        PmDomain = pmDomain;
        Sources = sources ?? Array.Empty<string>();
        Targets = targets ?? Array.Empty<string>();
        SyncTargets = syncTargets ?? Array.Empty<string>();
        State = state;
    }
}

/// <summary>
/// Declares a class as a projector (events → external read model).
/// Spec HIGH-4.2.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ProjectorAttribute : Attribute
{
    public string Name { get; }
    public string[] Domains { get; }

    public ProjectorAttribute(string name, string[] domains)
    {
        Name = name;
        Domains = domains ?? Array.Empty<string>();
    }
}

/// <summary>
/// Declares a class as an upcaster (event-version migrator for a single
/// domain). Spec HIGH-4.2.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class UpcasterAttribute : Attribute
{
    public string Name { get; }
    public string Domain { get; }

    public UpcasterAttribute(string name, string domain)
    {
        Name = name;
        Domain = domain;
    }
}
