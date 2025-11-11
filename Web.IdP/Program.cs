using System.Globalization;
using Microsoft.AspNetCore.Localization;
using HybridIdP.Infrastructure.Identity;
using Infrastructure;
using Infrastructure.Identity;
using Infrastructure.Services;
using Core.Application;
using Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Razor;
using OpenIddict.Abstractions;
using Vite.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Web.IdP.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Add Vite services for Vue.js integration
builder.Services.AddViteServices();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    // Register the entity sets needed by OpenIddict
    options.UseOpenIddict<Guid>();
});
builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddPasswordValidator<DynamicPasswordValidator>() // Use custom dynamic password validator
    .AddDefaultTokenProviders() // Keep default token providers for other identity operations
    .AddErrorDescriber<LocalizedIdentityErrorDescriber>(); // Register your custom describer

// Configure cookie authentication to return 401/403 for API endpoints instead of redirecting
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = 401;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = 403;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

// Configure OpenIddict
builder.Services.AddOpenIddict()
    // Register the OpenIddict core components
    .AddCore(options =>
    {
        // Configure OpenIddict to use the Entity Framework Core stores and models
        options.UseEntityFrameworkCore()
            .UseDbContext<ApplicationDbContext>()
            .ReplaceDefaultEntities<Guid>();
    })
    // Register the OpenIddict server components
    .AddServer(options =>
    {
        // Enable the authorization and token endpoints
        options.SetAuthorizationEndpointUris("/connect/authorize")
               .SetTokenEndpointUris("/connect/token");

        // Enable the authorization code flow
        options.AllowAuthorizationCodeFlow()
               .RequireProofKeyForCodeExchange();

        // Register the signing and encryption credentials
        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();

        // Register the ASP.NET Core host and configure the ASP.NET Core-specific options
        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableStatusCodePagesIntegration();
    })
    // Register the OpenIddict validation components
    .AddValidation(options =>
    {
        // Import the configuration from the local OpenIddict server instance
        options.UseLocalServer();

        // Register the ASP.NET Core host
        options.UseAspNetCore();
    });

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");


builder.Services.AddMvc()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization();

// Branding options
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection("Branding"));

// Configure authorization with permission-based policies
builder.Services.AddAuthorization(options =>
{
    // Register a policy for each permission
    var permissions = Core.Domain.Constants.Permissions.GetAll();
    foreach (var permission in permissions)
    {
        options.AddPolicy(permission, policy =>
            policy.Requirements.Add(new Infrastructure.Authorization.PermissionRequirement(permission)));
    }
});

// Register permission authorization handler
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, 
    Infrastructure.Authorization.PermissionAuthorizationHandler>();

// Register Turnstile service and HttpClient
builder.Services.AddHttpClient();
builder.Services.AddScoped<ITurnstileService, TurnstileService>();
// JIT & Legacy services
builder.Services.AddScoped<IJitProvisioningService, JitProvisioningService>();
builder.Services.AddScoped<ILegacyAuthService, LegacyAuthService>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, MyUserClaimsPrincipalFactory>();
// Identity management services
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IRoleManagementService, RoleManagementService>();
builder.Services.AddScoped<IScopeService, ScopeService>();
builder.Services.AddScoped<IApiResourceService, ApiResourceService>();
// Settings + Branding services
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IBrandingService, BrandingService>();
builder.Services.AddScoped<ISecurityPolicyService, SecurityPolicyService>();
builder.Services.AddScoped<ILoginService, LoginService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Use Vite development server in development mode
if (app.Environment.IsDevelopment())
{
    app.UseViteDevelopmentServer();
}

app.UseRouting();

var supportedCultures = new[] { "zh-TW", "en-US" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])  // 預設為 zh-TW
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers(); // Map API controller endpoints
app.MapRazorPages()
   .WithStaticAssets();

// Seed the database
await DataSeeder.SeedAsync(app.Services);

app.Run();
