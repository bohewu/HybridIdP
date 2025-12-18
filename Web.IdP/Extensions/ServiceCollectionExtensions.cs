using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using global::Infrastructure;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.Events;
using Web.IdP.Infrastructure.Identity;
using HybridIdP.Infrastructure.Identity;
using global::Infrastructure.Identity;
using global::Infrastructure.Services;
using global::Infrastructure.Options;
using Core.Application;
using Core.Application.Options;
using Web.IdP.Services;
using Web.IdP.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Npgsql;
using Quartz;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation.AspNetCore;
using System.Security.Cryptography.X509Certificates; // For X509CertificateLoader
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Web.IdP;
using Web.IdP.Services.Localization;

namespace Web.IdP.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Core Services
        services.AddHttpContextAccessor();
        services.AddScoped<ITurnstileService, TurnstileService>();
        services.AddScoped<IJitProvisioningService, JitProvisioningService>();
        services.AddScoped<ILegacyAuthService, LegacyAuthService>();
        services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, MyUserClaimsPrincipalFactory>();

        // Identity Management
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IRoleManagementService, RoleManagementService>();
        services.AddScoped<IScopeService, ScopeService>();
        services.AddScoped<IPersonService, PersonService>();
        services.AddScoped<IPersonLifecycleService, PersonLifecycleService>(); // Phase 18
        services.AddScoped<ILocalizationService, LocalizationService>();
        services.AddScoped<ILocalizationManagementService, LocalizationManagementService>();
        services.AddScoped<IApiResourceService, ApiResourceService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IClientAllowedScopesService, ClientAllowedScopesService>();
        services.AddScoped<IClientScopeRequestProcessor, ClientScopeRequestProcessor>();
        services.AddScoped<IClaimsService, ClaimsService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IImpersonationService, ImpersonationService>();
        services.AddScoped<IMfaService, MfaService>(); // Phase 20: MFA
        services.AddScoped<Core.Application.Interfaces.IEmailTemplateService, EmailTemplateService>(); // Phase 20.3: Email MFA Templates

        // Connect/OIDC Services
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IUserInfoService, UserInfoService>();
        services.AddScoped<IIntrospectionService, IntrospectionService>();
        services.AddScoped<IRevocationService, RevocationService>();
        services.AddScoped<IDeviceFlowService, DeviceFlowService>();
        services.AddScoped<IClaimsEnrichmentService, ClaimsEnrichmentService>();

        // Settings & Infrastructure
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IBrandingService, BrandingService>();
        services.AddScoped<ISecurityPolicyService, SecurityPolicyService>();
        services.AddScoped<ILoginService, LoginService>();
        services.AddScoped<ILoginHistoryService, LoginHistoryService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAccountManagementService, AccountManagementService>();
        services.AddScoped<IMonitoringService, MonitoringService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IDynamicLoggingService, DynamicLoggingService>();

        // Domain Event Handlers
        services.AddScoped<IDomainEventHandler<UserCreatedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<UserUpdatedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<UserDeletedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<UserRoleAssignedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<UserPasswordChangedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<UserAccountStatusChangedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<ClientCreatedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<ClientUpdatedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<ClientDeletedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<ClientSecretChangedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<ClientScopeChangedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<RoleCreatedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<RoleUpdatedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<RoleDeletedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<RolePermissionChangedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<ScopeCreatedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<ScopeUpdatedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<ScopeDeletedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<ScopeClaimChangedEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<LoginAttemptEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<LogoutEvent>, AuditService>();
        services.AddScoped<IDomainEventHandler<SecurityPolicyUpdatedEvent>, AuditService>();
        
        services.AddScoped<INotificationService, FakeNotificationService>();
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();

        // Options Configuration
        services.Configure<AppInfoOptions>(configuration.GetSection(AppInfoOptions.Section));
        services.Configure<LegacyAuthOptions>(configuration.GetSection(LegacyAuthOptions.SectionName));
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.Section));
        services.Configure<AuditOptions>(configuration.GetSection(AuditOptions.SectionName));
        services.Configure<BrandingOptions>(configuration.GetSection(BrandingOptions.Section));
        services.Configure<TurnstileOptions>(configuration.GetSection(TurnstileOptions.Section));
        services.Configure<ObservabilityOptions>(options =>
        {
            configuration.GetSection(ObservabilityOptions.MonitoringSection).Bind(options);
            configuration.GetSection(ObservabilityOptions.ObservabilitySection).Bind(options);
        });
        services.Configure<ProxyOptions>(configuration.GetSection(ProxyOptions.Section));
        
        return services;
    }

    public static IServiceCollection AddCustomObservability(this IServiceCollection services, IConfiguration configuration, string databaseProvider, string connectionString, string? redisConnectionString)
    {
        // Configure OpenTelemetry
        var appInfoOptions = new AppInfoOptions();
        configuration.GetSection(AppInfoOptions.Section).Bind(appInfoOptions);

        var serviceName = appInfoOptions.ServiceName;
        var serviceVersion = appInfoOptions.ServiceVersion;

        services.AddOpenTelemetry()
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
        var healthChecksBuilder = services.AddHealthChecks();

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

        // Add Redis Health Check if connection string is available
        // Note: Redis enabled check is done by caller via checking connectionString nullability/validity if desired
        // Or we check config here.
        // Assuming redisConnectionString is provided only if enabled+valid.
        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            healthChecksBuilder.AddRedis(redisConnectionString);
        }

        // Add Monitoring Background Service
        services.AddHostedService<global::Infrastructure.BackgroundServices.MonitoringBackgroundService>();

        return services;
    }

    public static IServiceCollection AddCustomIdentityAndAccess(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment, string databaseProvider, string connectionString)
    {
        // Add DbContext
        services.AddDbContext<ApplicationDbContext>(options =>
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
            options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
            options.UseOpenIddict<Guid>();
        });
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        // Add Identity
        services.AddIdentity<ApplicationUser, ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddPasswordValidator<DynamicPasswordValidator>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<LocalizedIdentityErrorDescriber>();

        // Configure SecurityStampValidatorOptions from configuration (e.g. for testing)
        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            var interval = configuration.GetValue<int?>("Security:ValidationIntervalSeconds");
            if (interval.HasValue)
            {
                options.ValidationInterval = TimeSpan.FromSeconds(interval.Value);
            }
        });

        // Configure Application Cookie
        var cookieOptions = new Options.CookieOptions();
        configuration.GetSection(Options.CookieOptions.Section).Bind(cookieOptions);
        
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Name = cookieOptions.GetIdentityCookieName();

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
        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<ApplicationDbContext>()
                    .ReplaceDefaultEntities<Guid>();
                options.UseQuartz();
            })
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("/connect/authorize")
                       .SetTokenEndpointUris("/connect/token")
                       .SetUserInfoEndpointUris("/connect/userinfo")
                       .SetIntrospectionEndpointUris("/connect/introspect")
                       .SetRevocationEndpointUris("/connect/revoke")
                       .SetPushedAuthorizationEndpointUris("/connect/par")
                       .SetEndSessionEndpointUris("/connect/logout")
                       .SetDeviceAuthorizationEndpointUris("/connect/device")
                       .SetEndUserVerificationEndpointUris("/connect/verify");

                var tokenOptions = new Web.IdP.Options.TokenOptions();
                configuration.GetSection(Web.IdP.Options.TokenOptions.SectionName).Bind(tokenOptions);

                options.AllowAuthorizationCodeFlow()
                       .RequireProofKeyForCodeExchange();

                options.AllowRefreshTokenFlow()
                       .SetRefreshTokenLifetime(TimeSpan.FromMinutes(tokenOptions.RefreshTokenLifetimeMinutes))
                       .SetRefreshTokenReuseLeeway(TimeSpan.FromSeconds(tokenOptions.RefreshTokenReuseLeewaySeconds));

                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(tokenOptions.AccessTokenLifetimeMinutes));

                options.AllowClientCredentialsFlow();

                // Resource Owner Password Credentials (ROPC) flow
                // Note: ROPC is discouraged for public clients but useful for testing
                options.AllowPasswordFlow();

                options.AllowDeviceAuthorizationFlow()
                       .SetDeviceCodeLifetime(TimeSpan.FromMinutes(tokenOptions.DeviceCodeLifetimeMinutes));

                var encryptionCertPath = configuration["Certificates:EncryptionCertificatePath"];
                var encryptionCertPassword = configuration["Certificates:EncryptionCertificatePassword"];
                var signingCertPath = configuration["Certificates:SigningCertificatePath"];
                var signingCertPassword = configuration["Certificates:SigningCertificatePassword"];

                if (!string.IsNullOrEmpty(encryptionCertPath) && File.Exists(encryptionCertPath) &&
                    !string.IsNullOrEmpty(signingCertPath) && File.Exists(signingCertPath))
                {
                    options.AddEncryptionCertificate(X509CertificateLoader.LoadPkcs12FromFile(encryptionCertPath, encryptionCertPassword));
                    options.AddSigningCertificate(X509CertificateLoader.LoadPkcs12FromFile(signingCertPath, signingCertPassword));
                }
                else
                {
                    options.AddDevelopmentEncryptionCertificate()
                           .AddDevelopmentSigningCertificate();
                }

                options.UseAspNetCore()
                       .EnableAuthorizationEndpointPassthrough()
                       .EnableTokenEndpointPassthrough()
                       .EnableUserInfoEndpointPassthrough()
                       .EnableEndSessionEndpointPassthrough()
                       .EnableEndUserVerificationEndpointPassthrough()
                       .EnableStatusCodePagesIntegration();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });
            
        // Configure Authorization
        services.AddAuthorization(options =>
        {
            var permissions = Core.Domain.Constants.Permissions.GetAll();
            foreach (var permission in permissions)
            {
                options.AddPolicy(permission, policy =>
                    policy.Requirements.Add(new global::Infrastructure.Authorization.PermissionRequirement(permission)));
            }

            options.AddPolicy("HasAnyAdminAccess", policy =>
            {
                policy.Requirements.Add(new global::Infrastructure.Authorization.HasAnyPermissionRequirement(
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

            options.AddPolicy("PrometheusMetrics", policy =>
            {
                policy.Requirements.Add(new global::Infrastructure.Authorization.IpWhitelistRequirement());
            });
        });

        // Register Authorization Handlers
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, global::Infrastructure.Authorization.PermissionAuthorizationHandler>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, global::Infrastructure.Authorization.HasAnyPermissionAuthorizationHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, global::Infrastructure.Authorization.IpWhitelistAuthorizationHandler>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, global::Infrastructure.Authorization.ScopeAuthorizationHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, global::Infrastructure.Authorization.ScopeAuthorizationPolicyProvider>();
        
        return services;
    }

    public static IServiceCollection AddCustomPlatformServices(this IServiceCollection services, IConfiguration configuration, bool redisEnabled, string? redisConnectionString)
    {
        // Custom Vite Manifest Service
        services.AddSingleton<IViteManifestService, ViteManifestService>();

        // Add SignalR
        services.AddSignalR();

        // Turnstile Services
        services.AddSingleton<ITurnstileStateService, TurnstileStateService>();
        services.AddHostedService<CloudflareConnectivityService>();
        services.AddHttpClient<CloudflareConnectivityService>();

        // Cache Configuration
        if (redisEnabled && !string.IsNullOrEmpty(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = configuration["Redis:InstanceName"] ?? "HybridIdP_";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // Quartz.NET
        services.AddQuartz(options =>
        {
            options.UseSimpleTypeLoader();
            options.UseInMemoryStore();

            // Phase 18.4: PersonLifecycleJob - runs daily at midnight to process scheduled transitions
            var jobKey = new JobKey(
                global::Infrastructure.Jobs.PersonLifecycleJobConstants.JobName,
                global::Infrastructure.Jobs.PersonLifecycleJobConstants.JobGroup);

            options.AddJob<global::Infrastructure.Jobs.PersonLifecycleJob>(opts => opts.WithIdentity(jobKey));
            options.AddTrigger(opts => opts
                .ForJob(jobKey)
                .WithIdentity(global::Infrastructure.Jobs.PersonLifecycleJobConstants.TriggerName)
                .WithCronSchedule(global::Infrastructure.Jobs.PersonLifecycleJobConstants.DefaultCronSchedule)
                .WithDescription("Daily job to process Person lifecycle transitions (auto-activate/terminate)"));
        });
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        // Session (reuse cookieOptions from identity setup scope - re-read for clarity)
        services.AddSession(options =>
        {
            var cookieOpts = new Options.CookieOptions();
            configuration.GetSection(Options.CookieOptions.Section).Bind(cookieOpts);
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Name = cookieOpts.GetSessionCookieName();
        });

        // Antiforgery
        services.AddAntiforgery(options =>
        {
            var cookieOpts = new Options.CookieOptions();
            configuration.GetSection(Options.CookieOptions.Section).Bind(cookieOpts);
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Name = cookieOpts.GetAntiforgeryCookieName();
        });

        // MVC & Localization support - using custom JSON localizer
        services.AddJsonLocalization(options =>
        {
            options.ResourcesPath = "Resources";
            options.AdditionalAssemblyPrefixes = new List<string> { "Infrastructure" };
        });

        services.AddMvc()
            .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
            .AddDataAnnotationsLocalization(options =>
            {
                options.DataAnnotationLocalizerProvider = (type, factory) =>
                    factory.Create(typeof(SharedResource));
            });

        return services;
    }

    public static IServiceCollection AddCustomRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var rateLimitingOptions = new RateLimitingOptions();
        configuration.GetSection(RateLimitingOptions.Section).Bind(rateLimitingOptions);

        if (rateLimitingOptions.Enabled)
        {
            services.AddRateLimiter(options =>
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
        return services;
    }
}
