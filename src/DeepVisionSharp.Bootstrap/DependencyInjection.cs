using Microsoft.Extensions.DependencyInjection;
using DeepVisionSharp.Application;
using DeepVisionSharp.Infrastructure;

namespace DeepVisionSharp.Bootstrap;

public static class DependencyInjection
{
    public static IServiceCollection AddDeepVision(this IServiceCollection services)
    {
        services.AddDeepVisionApplication();
        services.AddDeepVisionInfrastructure();
        return services;
    }
}
