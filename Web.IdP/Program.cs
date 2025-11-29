using System.Globalization;
using Microsoft.AspNetCore.Localization;
using HybridIdP.Infrastructure.Identity;
using Infrastructure;
using Infrastructure.Identity;
using Infrastructure.Services;
using Core.Application;
using Core.Domain;
using Core.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Razor;
using OpenIddict.Abstractions;
using Vite.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Web.IdP.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter.Prometheus;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Add Vite services for Vue.js integration
builder.Services.AddViteServices();

// Database provider selection: prioritize environment variable, then appsettings
var databaseProvider = Environment.GetEnvironmentVariable("DATABASE_PROVIDER") 
    ?? builder.Configuration["DatabaseProvider"] 
    ?? "SqlServer";

var connectionString = databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)
    ? builder.Configuration.GetConnectionString("PostgreSqlConnection") ?? throw new InvalidOperationException("Connection string 'PostgreSqlConnection' not found.")
    : builder.Configuration.GetConnectionString("SqlServerConnection") ?? throw new InvalidOperationException("Connection string 'SqlServerConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionString, sqlOptions => 
            sqlOptions.MigrationsAssembly("Infrastructure.Migrations.Postgres"));
    }
    else
    {
        options.UseSqlServer(connectionString, sqlOptions => 
            sqlOptions.MigrationsAssembly("Infrastructure.Migrations.SqlServer"));
    }
    // Ignore EF Core "PendingModelChangesWarning" at startup so migrations can be applied
    // at runtime without failing if the model snapshot differs slightly. This prevents
    // MigrateAsync from throwing when the model has harmless provider-specific annotations
    // or snapshot differences during local development/testing runs.
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
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
        if (context.Request.Path.StartsWithSegments("/api") || 
            context.Request.Path.StartsWithSegments("/metrics"))
        {
            context.Response.StatusCode = 401;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api") || 
            context.Request.Path.StartsWithSegments("/metrics"))
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
               .SetTokenEndpointUris("/connect/token")
               .SetUserInfoEndpointUris("/connect/userinfo");

        // Enable the authorization code flow
        options.AllowAuthorizationCodeFlow()
               .RequireProofKeyForCodeExchange();

        // Register the signing and encryption credentials
        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();

        // Register the ASP.NET Core host and configure the ASP.NET Core-specific options
        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableUserInfoEndpointPassthrough()
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

// Register HttpContextAccessor for IP-based authorization
builder.Services.AddHttpContextAccessor();

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

    // Register Prometheus metrics IP whitelist policy
    options.AddPolicy("PrometheusMetrics", policy =>
    {
        policy.Requirements.Add(new Infrastructure.Authorization.IpWhitelistRequirement());
    });
});

// Register permission authorization handler
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, 
    Infrastructure.Authorization.PermissionAuthorizationHandler>();

// Register IP whitelist authorization handler
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Infrastructure.Authorization.IpWhitelistAuthorizationHandler>();

// Register scope authorization handler and policy provider (Phase 9.2)
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Infrastructure.Authorization.ScopeAuthorizationHandler>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider,
    Infrastructure.Authorization.ScopeAuthorizationPolicyProvider>();

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
builder.Services.AddScoped<IPersonService, PersonService>(); // Phase 10.2
builder.Services.AddScoped<ILocalizationService, LocalizationService>();
builder.Services.AddScoped<IApiResourceService, ApiResourceService>();
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IClientAllowedScopesService, ClientAllowedScopesService>();
builder.Services.AddScoped<ClientScopeRequestProcessor>();
builder.Services.AddScoped<IClaimsService, ClaimsService>();
builder.Services.AddScoped<ISessionService, SessionService>();
// Settings + Branding services
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IBrandingService, BrandingService>();
builder.Services.AddScoped<ISecurityPolicyService, SecurityPolicyService>();
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<ILoginHistoryService, LoginHistoryService>();
builder.Services.AddScoped<IAuditService, AuditService>(); // Uses SettingsService for retention
builder.Services.AddScoped<IMonitoringService, MonitoringService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
// Domain Event Handlers
builder.Services.AddScoped<IDomainEventHandler<UserCreatedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<UserUpdatedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<UserDeletedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<UserRoleAssignedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<UserPasswordChangedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<UserAccountStatusChangedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<ClientCreatedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<ClientUpdatedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<ClientDeletedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<ClientSecretChangedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<ClientScopeChangedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<RoleCreatedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<RoleUpdatedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<RoleDeletedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<RolePermissionChangedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<ScopeCreatedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<ScopeUpdatedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<ScopeDeletedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<ScopeClaimChangedEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<LoginAttemptEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<LogoutEvent>, AuditService>();
builder.Services.AddScoped<IDomainEventHandler<SecurityPolicyUpdatedEvent>, AuditService>();
builder.Services.AddScoped<INotificationService, FakeNotificationService>();
builder.Services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();

// Add SignalR services
builder.Services.AddSignalR();

// Add Monitoring Background Service
// todo: 未來可以在安全設定裡面設定是否啟用，及秒數
// builder.Services.AddHostedService<Infrastructure.BackgroundServices.MonitoringBackgroundService>();

// Configure OpenTelemetry
var serviceName = "HybridAuthIdP";
var serviceVersion = "1.0.0";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: serviceName, serviceVersion: serviceVersion))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.Filter = httpContext =>
            {
                // Don't trace static assets and Vite HMR
                var path = httpContext.Request.Path.Value ?? string.Empty;
                return !path.StartsWith("/@") && !path.StartsWith("/node_modules");
            };
        })
        .AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;
        })
        .AddEntityFrameworkCoreInstrumentation(options =>
        {
            options.SetDbStatementForText = true;
            options.SetDbStatementForStoredProcedure = true;
        })
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddPrometheusExporter());

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

// Protect Prometheus metrics endpoint with IP whitelist authorization
if (builder.Configuration.GetValue<bool>("Observability:PrometheusEnabled"))
{
    app.MapPrometheusScrapingEndpoint()
        .RequireAuthorization("PrometheusMetrics");
}

app.MapStaticAssets();
app.MapControllers(); // Map API controller endpoints
app.MapHub<Infrastructure.Hubs.MonitoringHub>("/monitoringHub");
app.MapRazorPages()
   .WithStaticAssets();

// Seed the database
await DataSeeder.SeedAsync(app.Services);

app.Run();
