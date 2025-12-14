using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Web.IdP.Filters;
using Xunit;

namespace Tests.Application.UnitTests.Filters
{
    public class RequireClientPermissionFilterTests
    {
        private readonly Mock<IOpenIddictApplicationManager> _mockApplicationManager;
        private readonly RequireClientPermissionFilter _filter;
        private readonly string _testPermission = "test_permission";

        public RequireClientPermissionFilterTests()
        {
            _mockApplicationManager = new Mock<IOpenIddictApplicationManager>();
            _filter = new RequireClientPermissionFilter(_testPermission, _mockApplicationManager.Object);
        }

        [Fact]
        public async Task OnAuthorizationAsync_NoOpenIddictRequest_ReturnsForbidResult()
        {
            // Arrange
            var context = CreateContext(); // No features setup

            // Act
            await _filter.OnAuthorizationAsync(context);

            // Assert
            var result = Assert.IsType<ForbidResult>(context.Result);
            Assert.Contains(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, result.AuthenticationSchemes);
        }

        [Fact]
        public async Task OnAuthorizationAsync_NoClientId_ReturnsForbidResult()
        {
            // Arrange
            var request = new OpenIddictRequest(); // Empty request
            var context = CreateContext(request);

            // Act
            await _filter.OnAuthorizationAsync(context);

            // Assert
            var result = Assert.IsType<ForbidResult>(context.Result);
        }

        [Fact]
        public async Task OnAuthorizationAsync_ClientNotFound_ReturnsForbidResult()
        {
            // Arrange
            var clientId = "unknown_client";
            var request = new OpenIddictRequest { ClientId = clientId };
            var context = CreateContext(request);

            _mockApplicationManager.Setup(m => m.FindByClientIdAsync(clientId, default))
                .ReturnsAsync((object?)null);

            // Act
            await _filter.OnAuthorizationAsync(context);

            // Assert
            var result = Assert.IsType<ForbidResult>(context.Result);
        }

        [Fact]
        public async Task OnAuthorizationAsync_MissingPermission_ReturnsForbidResult()
        {
            // Arrange
            var clientId = "client";
            var client = new object();
            var request = new OpenIddictRequest { ClientId = clientId };
            var context = CreateContext(request);

            _mockApplicationManager.Setup(m => m.FindByClientIdAsync(clientId, default))
                .ReturnsAsync(client);
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(client, default))
                .ReturnsAsync(ImmutableArray.Create("other_permission"));

            // Act
            await _filter.OnAuthorizationAsync(context);

            // Assert
            var result = Assert.IsType<ForbidResult>(context.Result);
        }

        [Fact]
        public async Task OnAuthorizationAsync_HasPermission_ReturnsNullResult()
        {
            // Arrange
            var clientId = "client";
            var client = new object();
            var request = new OpenIddictRequest { ClientId = clientId };
            var context = CreateContext(request);

            _mockApplicationManager.Setup(m => m.FindByClientIdAsync(clientId, default))
                .ReturnsAsync(client);
            _mockApplicationManager.Setup(m => m.GetPermissionsAsync(client, default))
                .ReturnsAsync(ImmutableArray.Create(_testPermission, "other"));

            // Act
            await _filter.OnAuthorizationAsync(context);

            // Assert
            Assert.Null(context.Result); // Should pass through
        }

        private AuthorizationFilterContext CreateContext(OpenIddictRequest? request = null)
        {
            var httpContext = new DefaultHttpContext();
            
            if (request != null)
            {
                var transaction = new OpenIddictServerTransaction
                {
                    Request = request
                };
                var feature = new OpenIddictServerAspNetCoreFeature
                {
                    Transaction = transaction
                };
                httpContext.Features.Set(feature);
            }

            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor());

            return new AuthorizationFilterContext(
                actionContext,
                new List<IFilterMetadata>());
        }
    }
}
