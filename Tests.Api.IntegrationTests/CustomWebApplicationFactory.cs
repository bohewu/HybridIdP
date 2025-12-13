using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Infrastructure;
using Microsoft.VisualStudio.TestPlatform.TestHost; // Ensure Program is visible

namespace Tests.Api.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Use ConfigureTestServices which runs AFTER Program.cs ConfigureServices
        // This is the proper way to replace services in integration tests
        builder.ConfigureTestServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Remove ApplicationDbContext itself
            var contextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ApplicationDbContext));
            if (contextDescriptor != null)
            {
                services.Remove(contextDescriptor);
            }

            // Add InMemory Database
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase("InMemoryDbForTesting");
                options.EnableSensitiveDataLogging();
                options.ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
            });
        });

        builder.UseEnvironment("Test"); // Use Test environment to skip automatic seeding in Program.cs
        
        // Seed after host is built
        builder.ConfigureServices(services =>
        {
            // Add a hosted service that seeds the database on startup
            services.AddHostedService<DatabaseSeederHostedService>();
        });
    }

    // Hosted service to seed database after application starts
    private class DatabaseSeederHostedService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public DatabaseSeederHostedService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await DataSeeder.SeedAsync(scope.ServiceProvider, seedTestUsers: true);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
