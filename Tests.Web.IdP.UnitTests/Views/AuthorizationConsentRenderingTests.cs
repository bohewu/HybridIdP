using System.Text.Encodings.Web;
using Core.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Web.IdP.Controllers.Connect;
using Web.IdP.Services;

namespace Tests.Web.IdP.UnitTests.Views;

public class AuthorizationConsentRenderingTests
{
    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<svg onload=alert(1)>&\"category\"")]
    [InlineData("Custom category")]
    public async Task Consent_ShouldEncodeUnknownCategoryAndPreserveTranslatedGroups(string category)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { ApplicationName = typeof(AuthorizationController).Assembly.FullName });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AuthorizationController).Assembly);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton(Mock.Of<IBrandingService>(service => service.GetProductNameAsync() == Task.FromResult("HybridIdP")));
        builder.Services.AddSingleton(Mock.Of<IViteManifestService>());
        var localizer = new Mock<IViewLocalizer>();
        localizer.Setup(value => value[It.IsAny<string>()]).Returns((string key) =>
            new LocalizedHtmlString(key, key == "Identity" ? "Identity permissions" : key));
        builder.Services.AddSingleton(localizer.Object);
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var routes = new RouteData();
        routes.Routers.Add(new RouteCollection());
        var action = new ActionContext(http, routes, new ActionDescriptor());
        var result = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>()
            .GetView(null, "/Views/Authorization/Authorize.cshtml", true);
        Assert.True(result.Success);
        var data = new ViewDataDictionary<List<ScopeInfo>>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            { Model = [new ScopeInfo { Name = "profile", Category = "Identity" }, new ScopeInfo { Name = "email", Category = category }] };
        using var output = new StringWriter();
        await result.View.RenderAsync(new ViewContext(action, result.View, data,
            new TempDataDictionary(http, scope.ServiceProvider.GetRequiredService<ITempDataProvider>()), output, new HtmlHelperOptions()));
        var html = output.ToString();
        Assert.Contains(HtmlEncoder.Default.Encode(category), html);
        if (category.Contains('<')) Assert.DoesNotContain(category, html);
        Assert.Contains("Identity permissions", html);
        Assert.Contains("value=\"allow\"", html);
    }
}
