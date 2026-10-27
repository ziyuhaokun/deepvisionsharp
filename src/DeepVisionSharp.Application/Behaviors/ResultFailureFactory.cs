using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Behaviors;

internal static class ResultFailureFactory
{
    private static ILogger? _logger;

    public static void Initialize(ILogger logger)
    {
        _logger = logger;
    }

    public static bool IsResultResponseType(Type responseType)
    {
        if (responseType == typeof(Result))
        {
            return true;
        }

        return responseType.IsGenericType
            && responseType.GetGenericTypeDefinition() == typeof(Result<>);
    }

    /// <summary>
    /// Creates a <see cref="Result"/> / <see cref="Result{T}"/> failure for MediatR pipeline behaviors.
    /// Returns false when <typeparamref name="TResponse"/> is not a Result type — callers must rethrow
    /// instead of returning <c>default</c> (which silently nulls list/bool/entity queries).
    /// </summary>
    public static bool TryCreateFailureResponse<TResponse>(
        string errorMessage,
        [NotNullWhen(true)] out TResponse? response)
    {
        var responseType = typeof(TResponse);

        if (responseType == typeof(Result))
        {
            response = (TResponse)(object)Result.Failure(errorMessage);
            return true;
        }

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failureMethod = responseType.GetMethod(
                nameof(Result<object>.Failure),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(string)],
                modifiers: null);

            if (failureMethod is not null)
            {
                response = (TResponse)failureMethod.Invoke(null, [errorMessage])!;
                return true;
            }
        }

        _logger?.LogError(
            "MediatR pipeline cannot map failure to non-Result response type {ResponseType}. Error: {Error}",
            responseType.FullName,
            errorMessage);
        response = default;
        return false;
    }
}
