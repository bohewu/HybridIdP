using Core.Application.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Web.IdP.Services.Saml.Sp;

namespace Web.IdP.Extensions;

public static class SamlSpServiceCollectionExtensions
{
    public static IServiceCollection AddSamlSp(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Saml:Sp:Enabled")) return services;
        services.AddScoped<SamlSpResponseValidator>();
        services.AddScoped<SamlSpService>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, SamlSpAuthenticationHandler>(
            SamlProfile.AuthenticationScheme, configuration["Saml:Sp:DisplayName"] ?? "SAML", _ => { });
        return services;
    }
}
