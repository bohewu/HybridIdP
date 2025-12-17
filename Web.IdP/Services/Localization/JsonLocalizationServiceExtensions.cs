using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Web.IdP.Services.Localization;

/// <summary>
/// Extension methods for configuring JSON localization services.
/// </summary>
public static class JsonLocalizationServiceExtensions
{
    /// <summary>
    /// Adds JSON-based localization services to the specified IServiceCollection.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="configureOptions">An action to configure the JsonLocalizationOptions.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddJsonLocalization(
        this IServiceCollection services,
        Action<JsonLocalizationOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<JsonLocalizationOptions>(_ => { });
        }

        // Register the factory as singleton
        services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();

        // Also register the generic IStringLocalizer<T> for DI
        services.Add(new ServiceDescriptor(
            typeof(IStringLocalizer<>),
            typeof(StringLocalizer<>),
            ServiceLifetime.Transient));

        return services;
    }
}
