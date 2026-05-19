using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Angzarr.Client.Router;

namespace Angzarr.Client;

/// <summary>
/// Reflection-based handler-class introspection mirroring Java's
/// <c>HandlerMetadata</c>. Spec LOW-4.15 — enforces method-decorator
/// stacking rules: a single method may not carry conflicting kind
/// decorations (e.g. both <see cref="HandlesAttribute"/> and
/// <see cref="AppliesAttribute"/>).
/// </summary>
public static class HandlerMetadata
{
    /// <summary>
    /// Validate that no method on <paramref name="cls"/> stacks
    /// conflicting kind attributes. Spec LOW-4.15.
    /// Raises <see cref="BuildException"/> with
    /// <see cref="ErrorCodes.HandlerUnknownKind"/> when violated.
    /// </summary>
    public static void RequireNoStackedKindAttributes(Type cls)
    {
        if (cls == null) throw new ArgumentNullException(nameof(cls));
        var methods = cls.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly);
        foreach (var m in methods)
        {
            var kinds = new List<string>();
            if (m.GetCustomAttribute<HandlesAttribute>() != null) kinds.Add("Handles");
            if (m.GetCustomAttribute<AppliesAttribute>() != null) kinds.Add("Applies");
#pragma warning disable CS0618 // ProjectsAttribute deprecated per MED-4.12
            if (m.GetCustomAttribute<ProjectsAttribute>() != null) kinds.Add("Projects");
#pragma warning restore CS0618
            if (m.GetCustomAttribute<RejectedAttribute>() != null) kinds.Add("Rejected");
            if (m.GetCustomAttribute<UpcastsAttribute>() != null) kinds.Add("Upcasts");
            if (kinds.Count > 1)
            {
                throw new BuildException(
                    "method has multiple kind decorations stacked",
                    ErrorCodes.HandlerUnknownKind,
                    new Dictionary<string, string>
                    {
                        [ErrorKeys.HandlerClass] = cls.FullName ?? cls.Name,
                        [ErrorKeys.Field] = m.Name,
                        [ErrorKeys.HandlerKind] = kinds[0],
                        [ErrorKeys.OtherKind] = string.Join(",", kinds.Skip(1)),
                    });
            }
        }
    }

    /// <summary>
    /// Inspect a class for its class-level kind attribute(s) and return
    /// the discovered kind, or null when no class-level decoration is
    /// found. Throws <see cref="BuildException"/> with
    /// <see cref="ErrorCodes.MixedHandlerKinds"/> when multiple
    /// class-level kind attributes are stacked.
    /// </summary>
    public static RouterKind? KindOf(Type cls)
    {
        if (cls == null) throw new ArgumentNullException(nameof(cls));
        var found = new List<RouterKind>();
        if (cls.GetCustomAttribute<AggregateAttribute>() != null) found.Add(RouterKind.CommandHandler);
        if (cls.GetCustomAttribute<SagaAttribute>() != null) found.Add(RouterKind.Saga);
        if (cls.GetCustomAttribute<ProcessManagerAttribute>() != null) found.Add(RouterKind.ProcessManager);
        if (cls.GetCustomAttribute<ProjectorAttribute>() != null) found.Add(RouterKind.Projector);
        if (cls.GetCustomAttribute<UpcasterAttribute>() != null) found.Add(RouterKind.Upcaster);

        if (found.Count == 0) return null;
        if (found.Count > 1)
        {
            throw new BuildException(
                ErrorMessages.MixedHandlerKinds,
                ErrorCodes.MixedHandlerKinds,
                new Dictionary<string, string>
                {
                    [ErrorKeys.HandlerClass] = cls.FullName ?? cls.Name,
                    [ErrorKeys.HandlerKind] = found[0].ToString(),
                    [ErrorKeys.OtherKind] = string.Join(",", found.Skip(1).Select(k => k.ToString())),
                });
        }
        return found[0];
    }
}

/// <summary>
/// One-of label for the kind a built unified router contains.
/// Spec HIGH-4.4 — five-variant Built parity with Py/Rs/Ja.
/// </summary>
public enum RouterKind
{
    CommandHandler,
    Saga,
    ProcessManager,
    Projector,
    Upcaster,
}

/// <summary>
/// Cross-language convergence shape returned by
/// <see cref="UnifiedRouter.Build"/>. Mirrors Python's <c>Built</c>,
/// Rust's <c>Built</c>, Java's <c>Built</c>, Go's <c>Built</c> interface.
/// Spec HIGH-4.4 + audit #74.
/// </summary>
public sealed class Built
{
    public string RouterName { get; }
    public RouterKind Kind { get; }
    public int HandlerCount { get; }
    public IReadOnlyList<string> OutputDomains { get; }
    public IReadOnlyList<string> SyncOutputDomains { get; }
    public bool HasAsyncOutputs { get; }

    /// <summary>The wrapped typed router. Callers cast based on <see cref="Kind"/>.</summary>
    public object Inner { get; }

    public Built(
        string routerName,
        RouterKind kind,
        int handlerCount,
        IReadOnlyList<string> outputDomains,
        IReadOnlyList<string> syncOutputDomains,
        bool hasAsyncOutputs,
        object inner)
    {
        RouterName = routerName;
        Kind = kind;
        HandlerCount = handlerCount;
        OutputDomains = outputDomains;
        SyncOutputDomains = syncOutputDomains;
        HasAsyncOutputs = hasAsyncOutputs;
        Inner = inner;
    }
}

/// <summary>
/// Aggregator on top of the existing typed routers
/// (<see cref="CommandHandlerRouter{TState,THandler}"/>,
/// <see cref="SagaRouter{THandler}"/>,
/// <see cref="ProcessManagerRouter{TState}"/>,
/// <see cref="ProjectorRouter"/>,
/// <see cref="UpcasterRouter"/>).
///
/// <para>Spec HIGH-4.1/HIGH-4.4 (C#-side companion to D-Go's
/// <c>router_aggregator.go</c>): the typed routers stay as the ergonomic
/// API; <see cref="UnifiedRouter"/> performs cross-cutting validation
/// (<c>MIXED_HANDLER_KINDS</c>, <c>DUPLICATE_COMMAND_HANDLER</c>,
/// <c>ROUTER_NO_HANDLERS</c>) and returns a <see cref="Built"/>
/// inspectable by the per-kind <c>RunXxxServer</c> runners and
/// readiness/sync wiring (audit #74).</para>
/// </summary>
public sealed class UnifiedRouter
{
    private readonly string _name;
    private RouterKind? _kind;
    private readonly List<object> _registered = new();
    private readonly List<string> _outputDomains = new();
    private readonly List<string> _syncOutputDomains = new();
    private bool _hasAsyncOutputs;
    // Track (domain, type_url) pairs to detect duplicate command-handler
    // registrations across multiple typed routers.
    private readonly HashSet<(string Domain, string TypeUrl)> _commandKeys = new();

    public UnifiedRouter(string name)
    {
        _name = name;
    }

    public string Name => _name;

    /// <summary>
    /// Register a typed command handler. Spec HIGH-4.4.
    /// </summary>
    public UnifiedRouter AddCommandHandler<TState, THandler>(
        CommandHandlerRouter<TState, THandler> router)
        where TState : new()
        where THandler : ICommandHandlerDomainHandler<TState>
    {
        SetOrAssertKind(RouterKind.CommandHandler);
        // Duplicate-CH guard (audit #62).
        foreach (var typeUrl in router.CommandTypes())
        {
            var key = (router.Domain, typeUrl);
            if (!_commandKeys.Add(key))
            {
                throw new BuildException(
                    ErrorMessages.DuplicateCommandHandler,
                    ErrorCodes.DuplicateCommandHandler,
                    new Dictionary<string, string>
                    {
                        [ErrorKeys.RouterName] = _name,
                        [ErrorKeys.Domain] = router.Domain,
                        [ErrorKeys.TypeUrl] = typeUrl,
                    });
            }
        }
        _registered.Add(router);
        return this;
    }

    /// <summary>Register a saga router. Spec HIGH-4.4.</summary>
    public UnifiedRouter AddSaga<THandler>(SagaRouter<THandler> router)
        where THandler : ISagaDomainHandler
    {
        SetOrAssertKind(RouterKind.Saga);
        if (!string.IsNullOrEmpty(router.TargetDomain))
        {
            _outputDomains.Add(router.TargetDomain);
            _syncOutputDomains.Add(router.TargetDomain);
        }
        _registered.Add(router);
        return this;
    }

    /// <summary>Register a process manager router. Spec HIGH-4.4.</summary>
    public UnifiedRouter AddProcessManager<TState>(ProcessManagerRouter<TState> router)
        where TState : new()
    {
        SetOrAssertKind(RouterKind.ProcessManager);
        _hasAsyncOutputs = true;
        _registered.Add(router);
        return this;
    }

    /// <summary>Register a projector router. Spec HIGH-4.4.</summary>
    public UnifiedRouter AddProjector(ProjectorRouter router)
    {
        SetOrAssertKind(RouterKind.Projector);
        _registered.Add(router);
        return this;
    }

    /// <summary>Register an upcaster router. Spec HIGH-4.4.</summary>
    public UnifiedRouter AddUpcaster(UpcasterRouter router)
    {
        SetOrAssertKind(RouterKind.Upcaster);
        _registered.Add(router);
        return this;
    }

    private void SetOrAssertKind(RouterKind kind)
    {
        if (!_kind.HasValue)
        {
            _kind = kind;
            return;
        }
        if (_kind.Value != kind)
        {
            // Spec audit #72.
            throw new BuildException(
                ErrorMessages.MixedHandlerKinds,
                ErrorCodes.MixedHandlerKinds,
                new Dictionary<string, string>
                {
                    [ErrorKeys.RouterName] = _name,
                    [ErrorKeys.HandlerKind] = _kind.Value.ToString(),
                    [ErrorKeys.OtherKind] = kind.ToString(),
                });
        }
    }

    /// <summary>
    /// Build and validate. Spec HIGH-4.4. Raises
    /// <see cref="BuildException"/> with <see cref="ErrorCodes.RouterNoHandlers"/>
    /// when no router has been registered.
    /// </summary>
    public Built Build()
    {
        if (_registered.Count == 0 || !_kind.HasValue)
            throw new BuildException(
                ErrorMessages.RouterNoHandlers,
                ErrorCodes.RouterNoHandlers,
                new Dictionary<string, string>
                {
                    [ErrorKeys.RouterName] = _name,
                });

        // Surface a single Inner reference; for clusters with multiple typed
        // routers the caller can downcast and iterate.
        object inner = _registered.Count == 1 ? _registered[0] : _registered;

        return new Built(
            _name,
            _kind.Value,
            _registered.Count,
            _outputDomains.Distinct().ToList(),
            _syncOutputDomains.Distinct().ToList(),
            _hasAsyncOutputs,
            inner);
    }
}
