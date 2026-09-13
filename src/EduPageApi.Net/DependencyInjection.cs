using EduPageApi;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers EduPage session creation services.</summary>
public static class EduPageServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton factory. Created sessions belong to the caller and must be disposed.
    /// Does not register or use IHttpClientFactory.
    /// </summary>
    public static IServiceCollection AddEduPageSessions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<EduPageSessionFactory>();
        return services;
    }
}
