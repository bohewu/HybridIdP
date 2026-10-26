using Core.Application.Interfaces;
using Core.Application.Options;
using Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Web.IdP.Services.Saml;

namespace Web.IdP.Extensions;

public static class SamlServiceCollectionExtensions
{
    /// <summary>
    /// Adds role-neutral foundation only. Role owners register their services/scheme independently.
    /// Does not call AddSaml2/UseSaml2, which install a separate cookie authentication default.
    /// </summary>
    public static IServiceCollection AddSamlFoundation(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new SamlFederationOptions();
        configuration.GetSection(SamlFederationOptions.Section).Bind(options);
        var runtime = new SamlFederationRuntime(options); // Enabled material fails startup eagerly.
        services.AddSingleton(options);
        services.AddSingleton(_ => runtime);
        if (options.Idp.Enabled || options.Sp.Enabled)
        {
            services.AddDataProtection();
            services.TryAddSingleton(TimeProvider.System);
            services.AddScoped<ISamlStateStore, SamlStateStore>();
        }
        return services;
    }
}
