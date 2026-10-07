using Core.Application;
using Core.Application.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Web.IdP.Controllers.Admin;
using Web.IdP.ViewModels;

namespace Tests.Application.UnitTests;

public class PersonsControllerAuthorizationTests
{
    [Theory]
    [InlineData("link")]
    [InlineData("unlink")]
    [InlineData("transfer")]
    public async Task PersonOperation_ShouldReturnForbidden_WithoutSuccessAudit_WhenServiceDeniesAuthority(string operation)
    {
        var persons = new Mock<IPersonService>();
        var audit = new Mock<IAuditService>();
        persons.Setup(service => service.LinkAccountToPersonAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        persons.Setup(service => service.UnlinkAccountFromPersonAsync(It.IsAny<Guid>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        persons.Setup(service => service.TransferAssetsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
        var controller = new PersonsController(persons.Object, Mock.Of<ILogger<PersonsController>>(),
            audit.Object, Mock.Of<IUserManagementService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = operation switch
        {
            "link" => await controller.LinkAccount(Guid.NewGuid(), new LinkAccountDto { UserId = Guid.NewGuid() }),
            "unlink" => await controller.UnlinkAccount(Guid.NewGuid()),
            _ => await controller.TransferAssets(Guid.NewGuid(), new TransferAssetsViewModel { TargetPersonId = Guid.NewGuid() })
        };

        Assert.IsType<ForbidResult>(result);
        audit.Verify(service => service.LogEventAsync(It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
