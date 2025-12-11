using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Web.IdP.Infrastructure.Identity;
using HybridIdP.Infrastructure.Identity;
using Infrastructure;
using Infrastructure.Identity;
using Infrastructure.Services;
using Infrastructure.Options;
using Core.Application;
using Core.Domain;
using Core.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Razor;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Vite.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
using Web.IdP.Options;
using Core.Application.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter.Prometheus;
using Web.IdP.Middleware;
using Web.IdP.Services;
using Web.IdP.Constants;

using Quartz;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using HealthChecks.NpgSql;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Security.Cryptography.X509Certificates;


var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
var levelSwitch = new LoggingLevelSwitch();
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .MinimumLevel.ControlledBy(levelSwitch)
    .WriteTo.Console());

// Register LoggingLevelSwitch for injection
builder.Services.AddSingleton(levelSwitch);

// Add services to the container.
// Add Vite services for Vue.js integration
// Add Vite services for Vue.js integration
builder.Services.AddViteServices();

// Register Turnstile Services (Circuit Breaker)
builder.Services.AddSingleton<ITurnstileStateService, TurnstileStateService>();
builder.Services.AddHostedService<CloudflareConnectivityService>();
builder.Services.AddHttpClient<CloudflareConnectivityService>();

// Configure Proxy Options (Phase 17)
builder.Services.Configure<ProxyOptions>(builder.Configuration.GetSection(ProxyOptions.Section));

// Configure Cache (Redis or Memory) (Phase 17)
var redisConnectionString = builder.Configuration.GetConnectionString("RedisConnection");
var redisEnabled = builder.Configuration.GetValue<bool>("Redis:Enabled");

if (redisEnabled && !string.IsNullOrEmpty(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "HybridIdP_";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

// Database provider selection: prioritize environment variable, then appsettings
var databaseProvider = Environment.GetEnvironmentVariable("DATABASE_PROVIDER")
    ?? builder.Configuration["DatabaseProvider"]
    ?? "SqlServer";

var connectionString = databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)
    ? builder.Configuration.GetConnectionString("PostgreSqlConnection") ?? throw new InvalidOperationException("Connection string 'PostgreSqlConnection' not found.")
    : builder.Configuration.GetConnectionString("SqlServerConnection") ?? throw new InvalidOperationException("Connection string 'SqlServerConnection' not found.");

// Configure Quartz.NET
builder.Services.AddQuartz(options =>
{
    options.UseSimpleTypeLoader();
    options.UseInMemoryStore();
});

// Register the Quartz.NET hosted service
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

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
    // Security: Set secure cookie attributes
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Name = CookieConstants.IdentityCookieName;

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

        // Enable Quartz.NET integration
        options.UseQuartz();
    })
    // Register the OpenIddict server components
    .AddServer(options =>
    {
        // Enable the authorization and token endpoints
        options.SetAuthorizationEndpointUris("/connect/authorize")
               .SetTokenEndpointUris("/connect/token")
               .SetUserInfoEndpointUris("/connect/userinfo")
               .SetIntrospectionEndpointUris("/connect/introspect")
               .SetRevocationEndpointUris("/connect/revoke")
               .SetDeviceAuthorizationEndpointUris("/connect/device")
               .SetEndUserVerificationEndpointUris("/connect/verify");

        // Bind TokenOptions
        var tokenOptions = new Web.IdP.Options.TokenOptions();
        builder.Configuration.GetSection(Web.IdP.Options.TokenOptions.SectionName).Bind(tokenOptions);

        // Enable the authorization code flow
        options.AllowAuthorizationCodeFlow()
               .RequireProofKeyForCodeExchange();

        // Enable refresh token flow (OpenIddict uses rolling tokens by default)
        options.AllowRefreshTokenFlow()
               // Set refresh token lifetime
               .SetRefreshTokenLifetime(TimeSpan.FromMinutes(tokenOptions.RefreshTokenLifetimeMinutes));

        // Set access token lifetime
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(tokenOptions.AccessTokenLifetimeMinutes));

        // Enable client credentials flow for M2M authentication
        options.AllowClientCredentialsFlow();

        // Enable device authorization flow
        options.AllowDeviceAuthorizationFlow()
               // Set device code lifetime
               .SetDeviceCodeLifetime(TimeSpan.FromMinutes(tokenOptions.DeviceCodeLifetimeMinutes));

        // Register the signing and encryption credentials
        // Phase 17: Production Certificate Support
        var encryptionCertPath = builder.Configuration["Certificates:EncryptionCertificatePath"];
        var encryptionCertPassword = builder.Configuration["Certificates:EncryptionCertificatePassword"];
        var signingCertPath = builder.Configuration["Certificates:SigningCertificatePath"];
        var signingCertPassword = builder.Configuration["Certificates:SigningCertificatePassword"];

        if (!string.IsNullOrEmpty(encryptionCertPath) && File.Exists(encryptionCertPath) &&
            !string.IsNullOrEmpty(signingCertPath) && File.Exists(signingCertPath))
        {
            // Production: Load from PFX using .NET 10 X509CertificateLoader
            options.AddEncryptionCertificate(X509CertificateLoader.LoadPkcs12FromFile(encryptionCertPath, encryptionCertPassword));
            options.AddSigningCertificate(X509CertificateLoader.LoadPkcs12FromFile(signingCertPath, signingCertPassword));
        }
        else
        {
            // Development/Fallback: Use Ephemeral/Development Certificates
            options.AddDevelopmentEncryptionCertificate()
                   .AddDevelopmentSigningCertificate();
        }

        // Register the ASP.NET Core host and configure the ASP.NET Core-specific options
        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableUserInfoEndpointPassthrough()
               .EnableEndUserVerificationEndpointPassthrough()
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


// Configure session security
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Name = CookieConstants.SessionCookieName;
});

// Configure antiforgery tokens security
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Name = CookieConstants.AntiforgeryCookieName;
});

builder.Services.AddMvc()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization();

// Branding options
builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection(BrandingOptions.Section));

// Register Turnstile Options
builder.Services.Configure<TurnstileOptions>(builder.Configuration.GetSection(TurnstileOptions.Section));

// Register Observability Options
builder.Services.Configure<ObservabilityOptions>(options =>
{
    // Bind "Monitoring" section if available (legacy support)
    builder.Configuration.GetSection(ObservabilityOptions.MonitoringSection).Bind(options);
    
    // Bind "Observability" section, overriding any overlapping values
    builder.Configuration.GetSection(ObservabilityOptions.ObservabilitySection).Bind(options);
});

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

    // Register HasAnyAdminAccess policy for shared admin landing pages
    // Allows access if user has ANY admin-level permission (clients, scopes, users, roles, etc.)
    options.AddPolicy("HasAnyAdminAccess", policy =>
    {
        policy.Requirements.Add(new Infrastructure.Authorization.HasAnyPermissionRequirement(
            Core.Domain.Constants.Permissions.Clients.Read,
            Core.Domain.Constants.Permissions.Scopes.Read,
            Core.Domain.Constants.Permissions.Users.Read,
            Core.Domain.Constants.Permissions.Roles.Read,
            Core.Domain.Constants.Permissions.Persons.Read,
            Core.Domain.Constants.Permissions.Claims.Read,
            Core.Domain.Constants.Permissions.Settings.Read,
            Core.Domain.Constants.Permissions.Audit.Read
        ));
    });

    // Register Prometheus metrics IP whitelist policy
    options.AddPolicy("PrometheusMetrics", policy =>
    {
        policy.Requirements.Add(new Infrastructure.Authorization.IpWhitelistRequirement());
    });
});

// Register permission authorization handler
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Infrastructure.Authorization.PermissionAuthorizationHandler>();

// Register HasAnyPermission authorization handler
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Infrastructure.Authorization.HasAnyPermissionAuthorizationHandler>();

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
builder.Services.AddScoped<IClientScopeRequestProcessor, ClientScopeRequestProcessor>();
builder.Services.AddScoped<IClaimsService, ClaimsService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IImpersonationService, ImpersonationService>();

// connect services
builder.Services.AddScoped<IAuthorizationService, AuthorizationService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IUserInfoService, UserInfoService>();
builder.Services.AddScoped<IIntrospectionService, IntrospectionService>();
builder.Services.AddScoped<IRevocationService, RevocationService>();
builder.Services.AddScoped<IDeviceFlowService, DeviceFlowService>();


// Settings + Branding services
// builder.Services.AddMemoryCache(); // Replaced by AddDistributedMemoryCache above or Redis
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IBrandingService, BrandingService>();
builder.Services.AddScoped<ISecurityPolicyService, SecurityPolicyService>();
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<ILoginHistoryService, LoginHistoryService>();
builder.Services.AddScoped<IAuditService, AuditService>(); // Uses SettingsService for retention
builder.Services.AddScoped<IAccountManagementService, AccountManagementService>(); // Phase 11.2
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
// Add Monitoring Background Service
// todo: 未來可以在安全設定裡面設定是否啟用，及秒數
builder.Services.AddHostedService<Infrastructure.BackgroundServices.MonitoringBackgroundService>();

// Add Dynamic Logging Services
builder.Services.AddScoped<IDynamicLoggingService, DynamicLoggingService>();
builder.Services.AddHostedService<LogSettingsSyncService>();

// Register AppInfo Options
builder.Services.Configure<AppInfoOptions>(builder.Configuration.GetSection(AppInfoOptions.Section));

// Register LegacyAuth Options
builder.Services.Configure<LegacyAuthOptions>(builder.Configuration.GetSection(LegacyAuthOptions.SectionName));

// Register RateLimiting Options
builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection(RateLimitingOptions.Section));

// Register Audit Options (PII Masking Level)
builder.Services.Configure<AuditOptions>(builder.Configuration.GetSection(AuditOptions.SectionName));

// Configure Rate Limiting
var rateLimitingOptions = new RateLimitingOptions();
builder.Configuration.GetSection(RateLimitingOptions.Section).Bind(rateLimitingOptions);

if (rateLimitingOptions.Enabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        
        // Login endpoint policy - per IP address
        options.AddPolicy("login", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitingOptions.LoginPermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitingOptions.LoginWindowSeconds),
                    QueueLimit = rateLimitingOptions.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                }));
        
        // Token endpoint policy - per client ID
        options.AddPolicy("token", httpContext =>
        {
            var clientId = httpContext.Request.Form["client_id"].ToString();
            if (string.IsNullOrEmpty(clientId))
            {
                clientId = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            }
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: clientId,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitingOptions.TokenPermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitingOptions.TokenWindowSeconds),
                    QueueLimit = rateLimitingOptions.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
        });
        
        // Admin API policy - per client ID or IP
        options.AddPolicy("admin-api", httpContext =>
        {
            var clientId = httpContext.User?.FindFirst("client_id")?.Value;
            if (string.IsNullOrEmpty(clientId))
            {
                clientId = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            }
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: clientId,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitingOptions.AdminApiPermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitingOptions.AdminApiWindowSeconds),
                    QueueLimit = rateLimitingOptions.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
        });
    });
}

// Configure OpenTelemetry
var appInfoOptions = new AppInfoOptions();
builder.Configuration.GetSection(AppInfoOptions.Section).Bind(appInfoOptions);

var serviceName = appInfoOptions.ServiceName;
var serviceVersion = appInfoOptions.ServiceVersion;

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

// Add Health Checks
var healthChecksBuilder = builder.Services.AddHealthChecks();

if (databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
{
    healthChecksBuilder.AddCheck("Database (PostgreSQL)", () =>
    {
        try
        {
            using var conn = new Npgsql.NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.ExecuteScalar();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }, tags: new[] { "db", "postgresql" });
}
else
{
    healthChecksBuilder.AddSqlServer(connectionString);
}

// Add Redis Health Check if connection string is available AND Redis is enabled
// var redisConnectionString = builder.Configuration.GetConnectionString("RedisConnection"); // Moved up
if (redisEnabled && !string.IsNullOrEmpty(redisConnectionString))
{
    healthChecksBuilder.AddRedis(redisConnectionString);
}

// Add Health Checks UI - DISABLED due to incompatiblity with .NET 10 EF Core
// builder.Services.AddHealthChecksUI(setup =>
// {
//     setup.SetEvaluationTimeInSeconds(60); // Configurable
//     setup.AddHealthCheckEndpoint("HybridIdP Health", "/health");
// }).AddInMemoryStorage();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Add security headers middleware
app.UseSecurityHeaders();

// Configure Forwarded Headers Middleware (Phase 17)
// Required for running behind a proxy (Nginx, Load Balancer) to get real Client IP
var proxyOptions = new ProxyOptions();
builder.Configuration.GetSection(ProxyOptions.Section).Bind(proxyOptions);

if (proxyOptions.Enabled)
{
    var options = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };

    // Add KnownProxies
    if (!string.IsNullOrEmpty(proxyOptions.KnownProxies))
    {
        foreach (var ip in proxyOptions.KnownProxies.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (IPAddress.TryParse(ip, out var address))
            {
                options.KnownProxies.Add(address);
            }
        }
    }

    // Add KnownNetworks if supported (custom logic needed or Loopback default)
    // For simplicity here, we clear KnownProxies/Networks if we want to trust all (Internal Deployment)
    // Or we configure explicit IPs.
    // If KnownNetworks logic is complex, usually recommended to configure Nginx to set headers correctly and trust Nginx IP.
    
    // Explicitly set ForwardLimit
    options.ForwardLimit = proxyOptions.ForwardLimit;

    // Optional: RequireHeaderSymmetry
    options.RequireHeaderSymmetry = proxyOptions.RequireHeaderSymmetry;

    app.UseForwardedHeaders(options);
}

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

// Use rate limiting middleware if enabled
if (rateLimitingOptions.Enabled)
{
    app.UseRateLimiter();
}

app.UseAuthentication();
app.UseAuthorization();

// Protect Prometheus metrics endpoint with IP whitelist authorization
var observabilityOptions = new ObservabilityOptions();
builder.Configuration.GetSection(ObservabilityOptions.MonitoringSection).Bind(observabilityOptions);
builder.Configuration.GetSection(ObservabilityOptions.ObservabilitySection).Bind(observabilityOptions);

if (observabilityOptions.PrometheusEnabled)
{
    app.MapPrometheusScrapingEndpoint()
        .RequireAuthorization("PrometheusMetrics");
}

// Map Health Checks Endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Map Health Checks UI - DISABLED
// app.MapHealthChecksUI(options =>
// {
//     options.UIPath = "/health-ui";
//     options.ApiPath = "/health-ui-api";
// }).RequireAuthorization("HasAnyAdminAccess");

app.MapStaticAssets();
app.MapControllers(); // Map API controller endpoints
app.MapHub<Infrastructure.Hubs.MonitoringHub>("/monitoringHub");
app.MapRazorPages()
   .WithStaticAssets();

// Seed the database (seed test users only in development environment)
await DataSeeder.SeedAsync(app.Services, seedTestUsers: app.Environment.IsDevelopment());

app.Run();
