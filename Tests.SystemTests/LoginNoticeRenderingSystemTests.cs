using System.Text.Encodings.Web;
using Core.Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Web.IdP.Middleware;

namespace Tests.SystemTests;

public sealed class LoginNoticeRenderingSystemTests
{
    [Theory]
    [InlineData("<script data-loginnote=\"script\">notice()</script>")]
    [InlineData("<img src=x onerror=\"notice()\" data-loginnote=\"event\">")]
    [InlineData("<svg><p data-loginnote=\"malformed\"><script>notice()</script>")]
    public async Task LoginPage_EncodesConfiguredNotices_WhenValueContainsMarkup(string notice)
    {
        await using var factory = await LoginNoticeHostFactory.CreateAsync(notice, notice, notice);

        var response = await factory.Client.GetAsync("/Account/Login");
        var content = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, "The login page should render successfully.");
        var encodedNotice = HtmlEncoder.Default.Encode(notice);
        Assert.Equal(3, CountOccurrences(content, encodedNotice));
        Assert.Equal(0, CountOccurrences(content, notice));
    }

    [Fact]
    public async Task LoginPage_PreservesReadablePlainTextSpecialCharactersAndNewlines()
    {
        const string notice = "Plain text & <readable> \"quotes\"\nSecond line";
        await using var factory = await LoginNoticeHostFactory.CreateAsync(notice, notice, notice);

        var response = await factory.Client.GetAsync("/Account/Login");
        var content = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, "The login page should render successfully.");
        Assert.Equal(3, CountOccurrences(content, HtmlEncoder.Default.Encode(notice)));
        Assert.Equal(0, CountOccurrences(content, "<readable>"));
    }

    [Fact]
    public async Task LoginPage_RendersResolvedLocalizedValueAndEnUsFallback()
    {
        await using var factory = await LoginNoticeHostFactory.CreateAsync(
            "@LoginNotice.Localized",
            "@LoginNotice.Fallback",
            null);

        var response = await factory.Client.GetAsync("/Account/Login?culture=zh-TW");
        var content = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, "The login page should render successfully.");
        Assert.Equal(1, CountOccurrences(content, "Localized &amp; readable"));
        Assert.Equal(1, CountOccurrences(content, "Fallback &amp; readable"));
    }

    private static int CountOccurrences(string value, string expected)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(expected, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += expected.Length;
        }

        return count;
    }

    private sealed class LoginNoticeHostFactory : WebApplicationFactory<SecurityHeadersMiddleware>
    {
        private static readonly SemaphoreSlim EnvironmentLock = new(1, 1);
        private readonly string _databaseName = $"login-notice-{Guid.NewGuid():N}";
        private readonly string? _topMessage;
        private readonly string? _formMessage;
        private readonly string? _bottomMessage;

        private LoginNoticeHostFactory(string? topMessage, string? formMessage, string? bottomMessage)
        {
            _topMessage = topMessage;
            _formMessage = formMessage;
            _bottomMessage = bottomMessage;
        }

        public HttpClient Client { get; private set; } = null!;

        public static async Task<LoginNoticeHostFactory> CreateAsync(
            string? topMessage,
            string? formMessage,
            string? bottomMessage)
        {
            var factory = new LoginNoticeHostFactory(topMessage, formMessage, bottomMessage);
            await EnvironmentLock.WaitAsync();
            const string providerVariable = "DATABASE_PROVIDER";
            const string connectionVariable = "ConnectionStrings__SqlServerConnection";
            var previousProvider = Environment.GetEnvironmentVariable(providerVariable);
            var previousConnection = Environment.GetEnvironmentVariable(connectionVariable);
            try
            {
                Environment.SetEnvironmentVariable(providerVariable, "SqlServer");
                Environment.SetEnvironmentVariable(connectionVariable, "Server=(local);Database=unused");
                factory.Client = factory.CreateClient(new WebApplicationFactoryClientOptions
                {
                    AllowAutoRedirect = false,
                    BaseAddress = new Uri("https://localhost")
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable(providerVariable, previousProvider);
                Environment.SetEnvironmentVariable(connectionVariable, previousConnection);
                EnvironmentLock.Release();
            }

            try
            {
                await factory.SeedLocalizationAsync();
                return factory;
            }
            catch
            {
                await factory.DisposeAsync();
                throw;
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabaseProvider"] = "SqlServer",
                    ["ConnectionStrings:SqlServerConnection"] = "Server=(local);Database=unused",
                    ["Redis:Enabled"] = "false",
                    ["RateLimiting:Enabled"] = "false",
                    ["Turnstile:Enabled"] = "false",
                    ["SeedData:PrivilegedTestAdminBootstrap:Enabled"] = "false",
                    ["LoginNotices:TopMessage"] = _topMessage,
                    ["LoginNotices:FormMessage"] = _formMessage,
                    ["LoginNotices:BottomMessage"] = _bottomMessage
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.RemoveAll<IHostedService>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            });
        }

        private async Task SeedLocalizationAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.Resources.AddRange(
                new Resource
                {
                    Key = "LoginNotice.Localized",
                    Culture = "zh-TW",
                    Value = "Localized & readable",
                    IsEnabled = true
                },
                new Resource
                {
                    Key = "LoginNotice.Localized",
                    Culture = "en-US",
                    Value = "Localized fallback",
                    IsEnabled = true
                },
                new Resource
                {
                    Key = "LoginNotice.Fallback",
                    Culture = "en-US",
                    Value = "Fallback & readable",
                    IsEnabled = true
                });
            await dbContext.SaveChangesAsync();
        }

        public override async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await base.DisposeAsync();
        }
    }
}
