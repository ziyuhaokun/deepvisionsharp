using MediatR;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Common;

/// <summary>
/// CQRS request contracts (channel A). Routed via <c>IMediator</c> and Application pipeline behaviors.
/// Long-lived sessions, process hosts, and UI hot paths use Ports / workflow services instead (channel B).
/// </summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

public interface ICommand : IRequest<Result>;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;

public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
