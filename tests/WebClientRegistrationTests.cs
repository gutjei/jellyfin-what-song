using Jellyfin.Plugin.SongPause;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class WebClientRegistrationTests
{
    [Theory]
    [InlineData("own", true)]
    [InlineData("other-user", false)]
    [InlineData("anonymous", false)]
    [InlineData("missing", false)]
    public async Task CanOnlyRegisterTheAuthenticatedUsersSession(string scenario, bool allowed)
    {
        var sessions = new Mock<ISessionManager>();
        var auth = new Mock<IAuthorizationContext>();
        var registry = new WebClientRegistry();
        var userId = Guid.NewGuid();
        await using var session = new SessionInfo(sessions.Object, NullLogger.Instance)
        {
            Id = "session", UserId = scenario == "anonymous" ? Guid.Empty : scenario == "other-user" ? Guid.NewGuid() : userId
        };
        auth.Setup(x => x.GetAuthorizationInfo(It.IsAny<HttpRequest>())).ReturnsAsync(new AuthorizationInfo
        {
            User = new User("test", "auth", "reset") { Id = userId },
            DeviceId = "device", Client = "client", Version = "version"
        });
        sessions.Setup(x => x.GetSession("device", "client", "version")).Returns(scenario == "missing" ? null! : session);
        var controller = new SongsController(Mock.Of<ILibraryManager>(), Mock.Of<IUserManager>(),
            NullLogger<SongsController>.Instance, auth.Object, sessions.Object, registry)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var response = await controller.RegisterWebClient();
        if (allowed) Assert.IsType<NoContentResult>(response);
        else Assert.IsType<ForbidResult>(response);
        Assert.Equal(allowed, registry.IsActive(session.Id));
        Assert.False(registry.IsActive("another-session"));
    }
}
