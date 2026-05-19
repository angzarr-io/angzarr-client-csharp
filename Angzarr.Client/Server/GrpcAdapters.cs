using System;
using System.Threading.Tasks;
using Angzarr.Client.Router;
using Google.Protobuf;
using Grpc.Core;

namespace Angzarr.Client.Server;

/// <summary>
/// Shared status-code translation for the gRPC servicer adapters.
///
/// <para>Mirrors Python <c>router/server.py::_translate_and_abort</c> and
/// Rust <c>handler::client_error_to_status</c> — same logical failure
/// surfaces the same gRPC code on either side of the wire.</para>
///
/// <para>Audit #87: decode-time payload errors (the dispatch layer's
/// <c>InvalidProtocolBufferException</c> wrapping) surface as
/// <c>INVALID_ARGUMENT</c>, matching Python's <c>_parse_any</c> /
/// <c>_unpack_any</c> guards. This is a framework-level guarantee — the
/// servicer adapters catch the typed exception from the dispatch layer
/// and translate; user handler code does not need to wrap its own
/// decodes.</para>
/// </summary>
public static class GrpcStatusTranslator
{
    public static RpcException ToRpcException(Exception exc)
    {
        return exc switch
        {
            CommandRejectedError rejected => new RpcException(
                new Status(MapStatusCode(rejected.StatusCode), rejected.Message)),
            InvalidArgumentError invalid => new RpcException(
                new Status(StatusCode.InvalidArgument, invalid.Message)),
            InvalidTimestampError invalid => new RpcException(
                new Status(StatusCode.InvalidArgument, invalid.Message)),
            GrpcError grpc => new RpcException(
                new Status(grpc.StatusCode, grpc.Message)),
            ConnectionError conn => new RpcException(
                new Status(StatusCode.Unavailable, conn.Message)),
            TransportError tx => new RpcException(
                new Status(StatusCode.Unavailable, tx.Message)),
            // Audit #87 / spec MED-1.7: decode failures from the dispatch
            // layer's Any.Unpack() / message parsing surface as
            // INVALID_ARGUMENT with the static canonical `ANY_DECODE_FAILED`
            // message. The dynamic protobuf parser text is appended as a
            // structured trailer detail so the static-string contract holds
            // for cross-language assertions while debugging remains possible.
            InvalidProtocolBufferException pbe => new RpcException(
                new Status(StatusCode.InvalidArgument, ErrorMessages.AnyDecodeFailed),
                trailers: new Metadata
                {
                    { "x-angzarr-code", ErrorCodes.AnyDecodeFailed },
                    { "x-angzarr-cause", pbe.Message },
                }),
            _ => new RpcException(new Status(StatusCode.Internal, exc.Message)),
        };
    }

    private static StatusCode MapStatusCode(string status) => status switch
    {
        "INVALID_ARGUMENT" => StatusCode.InvalidArgument,
        "NOT_FOUND" => StatusCode.NotFound,
        "FAILED_PRECONDITION" => StatusCode.FailedPrecondition,
        _ => StatusCode.FailedPrecondition,
    };
}

/// <summary>
/// gRPC service adapter for a <see cref="CommandHandlerRouter{TState,THandler}"/>.
///
/// <para>Mirrors Python <c>router/server.py::CommandHandlerGrpc</c> and
/// Rust <c>handler::CommandHandlerGrpc</c> — delegates <c>Handle</c> to
/// the router's <c>Dispatch</c> method and translates dispatch-layer
/// exceptions to gRPC status codes.</para>
/// </summary>
public class CommandHandlerGrpcAdapter<TState, THandler> : Angzarr.CommandHandlerService.CommandHandlerServiceBase
    where TState : new()
    where THandler : ICommandHandlerDomainHandler<TState>
{
    private readonly CommandHandlerRouter<TState, THandler> _router;

    public CommandHandlerGrpcAdapter(CommandHandlerRouter<TState, THandler> router)
    {
        _router = router;
    }

    public override Task<Angzarr.BusinessResponse> Handle(
        Angzarr.ContextualCommand request,
        ServerCallContext context)
    {
        try
        {
            return Task.FromResult(_router.Dispatch(request));
        }
        catch (Exception exc)
        {
            throw GrpcStatusTranslator.ToRpcException(exc);
        }
    }
}

/// <summary>
/// gRPC service adapter for a <see cref="SagaRouter{THandler}"/>.
/// </summary>
public class SagaGrpcAdapter<THandler> : Angzarr.SagaService.SagaServiceBase
    where THandler : ISagaDomainHandler
{
    private readonly SagaRouter<THandler> _router;

    public SagaGrpcAdapter(SagaRouter<THandler> router)
    {
        _router = router;
    }

    public override Task<Angzarr.SagaResponse> Handle(
        Angzarr.SagaHandleRequest request,
        ServerCallContext context)
    {
        try
        {
            var saga = _router.Dispatch(request.Source);
            return Task.FromResult(saga);
        }
        catch (Exception exc)
        {
            throw GrpcStatusTranslator.ToRpcException(exc);
        }
    }
}

/// <summary>
/// gRPC service adapter for a <see cref="ProcessManagerRouter{TState}"/>.
/// </summary>
public class ProcessManagerGrpcAdapter<TState> : Angzarr.ProcessManagerService.ProcessManagerServiceBase
    where TState : new()
{
    private readonly ProcessManagerRouter<TState> _router;

    public ProcessManagerGrpcAdapter(ProcessManagerRouter<TState> router)
    {
        _router = router;
    }

    public override Task<Angzarr.ProcessManagerHandleResponse> Handle(
        Angzarr.ProcessManagerHandleRequest request,
        ServerCallContext context)
    {
        try
        {
            return Task.FromResult(
                _router.Dispatch(request.Trigger, request.ProcessState ?? new Angzarr.EventBook()));
        }
        catch (Exception exc)
        {
            throw GrpcStatusTranslator.ToRpcException(exc);
        }
    }
}

/// <summary>
/// gRPC service adapter for a <see cref="ProjectorRouter"/>.
/// </summary>
public class ProjectorGrpcAdapter : Angzarr.ProjectorService.ProjectorServiceBase
{
    private readonly ProjectorRouter _router;

    public ProjectorGrpcAdapter(ProjectorRouter router)
    {
        _router = router;
    }

    public override Task<Angzarr.Projection> Handle(
        Angzarr.EventBook request,
        ServerCallContext context)
    {
        try
        {
            return Task.FromResult(_router.Dispatch(request));
        }
        catch (Exception exc)
        {
            throw GrpcStatusTranslator.ToRpcException(exc);
        }
    }

    public override Task<Angzarr.Projection> HandleSpeculative(
        Angzarr.EventBook request,
        ServerCallContext context)
    {
        return Handle(request, context);
    }
}
