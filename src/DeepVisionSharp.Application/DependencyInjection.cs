using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Behaviors;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Models;

namespace DeepVisionSharp.Application;

/// <summary>
/// Application composition: MediatR CQRS + FluentValidation + pipeline behaviors.
/// Shell may also inject Ports / workflow services directly for long sessions and hot paths
/// (channel B; bypasses MediatR pipeline by design).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddDeepVisionApplication(this IServiceCollection services)
    {
        // Channel A: transactional CQRS. MediatR runs behaviors in registration order, outermost
        // first, so the exception mapper must be registered first: validators call into repositories
        // (a SQLite lock or IO error inside MustAsync) and their exceptions have to reach it too.
        services.AddMediatR(typeof(DependencyInjection));
        services.AddValidatorsFromAssemblyContaining(typeof(DependencyInjection));

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
        services.AddSingleton<ITaskCapabilityResolver, TaskCapabilityResolver>();
        services.AddSingleton<Ports.ITilingService, Services.TilingService>();
        services.Configure<PerformanceBehaviorOptions>(options =>
        {
            options.WarningThresholdMilliseconds = 500;
        });

        // ResultFailureFactory is a static helper called from pipeline behaviors, so it cannot take
        // an ILogger by injection. It is initialized by the behaviors themselves (they are always
        // constructed for a request and already hold a logger), which is why no registration here
        // is needed — the previous one returned an ILogger nobody resolved, so Initialize never ran.

        return services;
    }
}
