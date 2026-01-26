using Microsoft.Extensions.DependencyInjection;

using System;

namespace Core.Extensions;
public static class ServiceCollectionExtension
{
    public static IServiceCollection ForwardSingleton<TService, TInterface>(
        this IServiceCollection services)
        where TService : class, TInterface
    {
        services.AddSingleton(typeof(TService));
        services.AddSingleton(typeof(TInterface), GetRequired);

        return services;

        static TService GetRequired(IServiceProvider x)
            => x.GetRequiredService<TService>();
    }

    public static IServiceCollection ForwardSingleton<TService>(
        this IServiceCollection services, IServiceProvider sp)
        where TService : class
    {
        return services.AddSingleton(sp.GetRequiredService<TService>());
    }

    public static IServiceCollection ForwardSingleton<
        TService, TInterface>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> implementationFactory)
        where TService : class, TInterface
    {
        services.AddSingleton(typeof(TService), implementationFactory);
        services.AddSingleton(typeof(TInterface), GetRequired);

        return services;

        static TService GetRequired(IServiceProvider x)
            => x.GetRequiredService<TService>();
    }

    public static IServiceCollection ForwardSingleton<
        TService,
        TInterface1, TInterface2,
        TImplementation>(this IServiceCollection services)
        where TService : class, TInterface1, TInterface2
        where TImplementation : class, TService
    {
        services.AddSingleton(typeof(TService), typeof(TImplementation));
        services.AddSingleton(typeof(TInterface1), GetRequired);
        services.AddSingleton(typeof(TInterface2), GetRequired);

        return services;

        static TService GetRequired(IServiceProvider x)
            => x.GetRequiredService<TService>();
    }
}
