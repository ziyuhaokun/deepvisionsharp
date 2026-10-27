using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

    public ValidationBehavior(
        IEnumerable<IValidator<TRequest>> validators,
        ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    {
        _validators = validators;
        _logger = logger;
        // ResultFailureFactory is a static helper; this behavior is constructed for every request
        // that has validators, so initializing here guarantees the helper has a logger before it is
        // ever asked to map a failure.
        ResultFailureFactory.Initialize(logger);
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var validationResults = await Task.WhenAll(
            _validators.Select(validator => validator.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count == 0)
        {
            return await next();
        }

        var requestName = typeof(TRequest).Name;
        var errorMessage = string.Join("; ", failures.Select(failure => failure.ErrorMessage));
        _logger.LogWarning("验证失败：{RequestName} - {Errors}", requestName, errorMessage);

        if (ResultFailureFactory.TryCreateFailureResponse<TResponse>(errorMessage, out var failureResponse))
        {
            return failureResponse;
        }

        // Non-Result response types: throw so callers see a real failure (never default/null).
        throw new ValidationException(failures);
    }
}
