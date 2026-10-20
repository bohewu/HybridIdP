using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;
using Web.IdP;
using Web.IdP.Helpers;

namespace Tests.Web.IdP.UnitTests.Views;

public sealed class SystemInfoRenderingTests
{
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("ApplicationManager", false)]
    [InlineData("User", false)]
    [InlineData(null, false)]
    public async Task SystemInfo_ShouldRenderBuildVersionOnlyForAdmin(string? role, bool visible)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { ApplicationName = typeof(SharedResource).Assembly.FullName });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(SharedResource).Assembly);
        var localizer = new Mock<IStringLocalizer<SharedResource>>();
        localizer.Setup(value => value[It.IsAny<string>()])
            .Returns((string key) => new LocalizedString(key, key));
        builder.Services.AddSingleton(localizer.Object);
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (role is not null)
            http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "fixture"));
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var result = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>()
            .GetView(null, "/Pages/Shared/_SystemInfo.cshtml", false);
        Assert.True(result.Success);
        using var output = new StringWriter();
        await result.View.RenderAsync(new ViewContext(action, result.View,
            new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            new TempDataDictionary(http, scope.ServiceProvider.GetRequiredService<ITempDataProvider>()),
            output, new HtmlHelperOptions()));
        var html = output.ToString();
        Assert.Matches(@"^\d+\.\d+\.\d+(?:[-+][\w.-]+)?$", ProductVersionInfo.Version!);
        if (visible)
        {
            Assert.Contains("data-test-id=\"system-info\"", html);
            Assert.Contains(ProductVersionInfo.Version!, html);
            Assert.Contains("<summary", html);
            Assert.DoesNotContain(" open", html);
        }
        else
        {
            Assert.DoesNotContain("data-test-id=\"system-info\"", html);
            Assert.DoesNotContain(ProductVersionInfo.Version!, html);
        }
    }
}
