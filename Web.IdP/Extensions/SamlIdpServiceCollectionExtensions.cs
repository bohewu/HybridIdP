using Web.IdP.Services.Saml.Idp;

namespace Web.IdP.Extensions;

public static class SamlIdpServiceCollectionExtensions
{
    public static IServiceCollection AddSamlIdp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SamlAttributeService>();
        if (configuration.GetValue<bool>("Saml:Idp:Enabled"))
        {
            services.AddScoped<SamlIdpProtocol>();
            services.AddScoped<SamlIdpFlow>();
            services.AddScoped<ISamlIdpAccountPolicy, SamlIdpAccountPolicy>();
        }
        return services;
    }
}
