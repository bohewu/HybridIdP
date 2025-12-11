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

using static OpenIddict.Abstractions.OpenIddictConstants;
using Web.IdP.Options;
using Core.Application.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter.Prometheus;
using Web.IdP.Middleware;
using Web.IdP.Services;
using Web.IdP.Extensions;
using Web.IdP.Constants;
using Web.IdP; // Added for SharedResource

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
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false;
});

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

// Configure Cache (Redis or Memory) (Phase 17)
var redisConnectionString = builder.Configuration.GetConnectionString("RedisConnection");
var redisEnabled = builder.Configuration.GetValue<bool>("Redis:Enabled");

// Register Custom Platform Services (Vite, Turnstile, Cache, Quartz, Session, Antiforgery, MVC, RateLimiting)
builder.Services.AddCustomPlatformServices(builder.Configuration, redisEnabled, redisConnectionString);

// Database provider selection: prioritize environment variable, then appsettings
var databaseProvider = Environment.GetEnvironmentVariable("DATABASE_PROVIDER")
    ?? builder.Configuration["DatabaseProvider"]
    ?? "SqlServer";

var connectionString = databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)
    ? builder.Configuration.GetConnectionString("PostgreSqlConnection") ?? throw new InvalidOperationException("Connection string 'PostgreSqlConnection' not found.")
    : builder.Configuration.GetConnectionString("SqlServerConnection") ?? throw new InvalidOperationException("Connection string 'SqlServerConnection' not found.");

// Register Custom Identity and Access (DbContext, Identity, OpenIddict, Authorization)
builder.Services.AddCustomIdentityAndAccess(builder.Configuration, builder.Environment, databaseProvider, connectionString);

// Register Application Services and Options
builder.Services.AddCustomApplicationServices(builder.Configuration);

// Configure Rate Limiting
builder.Services.AddCustomRateLimiting(builder.Configuration);

// Register Observability
var finalRedisConn = (redisEnabled && !string.IsNullOrEmpty(redisConnectionString)) ? redisConnectionString : null;
builder.Services.AddCustomObservability(builder.Configuration, databaseProvider, connectionString, finalRedisConn);

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseCustomPipeline(builder.Configuration);
app.MapCustomEndpoints(builder.Configuration);

// Seed the database (seed test users only in development environment)
await DataSeeder.SeedAsync(app.Services, seedTestUsers: app.Environment.IsDevelopment());

app.Run();
