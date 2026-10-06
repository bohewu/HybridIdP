using Microsoft.AspNetCore.Mvc;
using TestClient.Constants;
using TestClient.Options;
using TestClient.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<OidcDemoOptions>()
    .BindConfiguration(OidcDemoOptions.Section)
    .Validate(options => Uri.TryCreate(options.Authority, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment), "Oidc:Authority must be an HTTPS issuer URL.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ClientId) &&
        options.Scopes is { Length: > 0 } && options.Scopes.Contains("openid") &&
        options.Scopes.All(scope => !string.IsNullOrWhiteSpace(scope) && !scope.Any(char.IsWhiteSpace)),
        "Oidc requires a client ID and nonempty scopes including openid.")
    .ValidateOnStart();
var configured = builder.Configuration.GetSection(OidcDemoOptions.Section).Get<OidcDemoOptions>() ?? new();
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = AuthenticationSchemes.Cookies;
    options.DefaultChallengeScheme = AuthenticationSchemes.OpenIdConnect;
})
.AddCookie(AuthenticationSchemes.Cookies, options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
})
.AddOpenIdConnect(AuthenticationSchemes.OpenIdConnect, options =>
{
    options.Authority = configured.Authority;
    options.ClientId = configured.ClientId;
    options.ResponseType = "code";
    options.ResponseMode = "query";
    options.UsePkce = true;
    options.SaveTokens = true;
    options.Scope.Clear();
    foreach (var scope in configured.Scopes) options.Scope.Add(scope);
    options.RequireHttpsMetadata = true;
    options.MapInboundClaims = false;
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.RoleClaimType = "role";

    options.Events.OnRemoteFailure = context =>
    {
        var message = context.Failure?.Message ?? string.Empty;
        if (message.Contains("access_denied", StringComparison.Ordinal))
            context.Response.Redirect("/Account/AccessDenied");
        else
        {
            var knownErrors = new[] { "invalid_scope", "invalid_request", "login_required", "consent_required", "interaction_required", "server_error" };
            var code = knownErrors.FirstOrDefault(code => message.Contains(code, StringComparison.Ordinal)) ?? "authentication_failed";
            context.Response.Redirect("/Account/AuthError?error=" + code);
        }
        context.HandleResponse();
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToIdentityProvider = context =>
    {
        context.Properties.Items.TryGetValue("demo_scenario", out var scenario);
        switch (scenario)
        {
            case "mfa": context.ProtocolMessage.AcrValues = "mfa"; break;
            case "fresh": context.ProtocolMessage.Prompt = "login"; break;
            case "max-age": context.ProtocolMessage.SetParameter("max_age", "0"); break;
            case "consent": context.ProtocolMessage.Prompt = "consent"; break;
            case "silent": context.ProtocolMessage.Prompt = "none"; break;
        }
        return Task.CompletedTask;
    };
});
builder.Services.AddHttpClient<OidcDemoService>(client => client.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.Run();
