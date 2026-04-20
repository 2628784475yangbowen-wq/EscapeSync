using EscapeSync.Server.Game;
using EscapeSync.Server.Hubs;
using EscapeSync.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace EscapeSync.Server.Tests;

public class GameHubTests
{
    private readonly GameHub _hub;

    public GameHubTests()
    {
        var hubCtx = new Mock<IHubContext<GameHub>>();
        var hubClients = new Mock<IHubClients>();
        var proxy = new Mock<IClientProxy>();
        var groupMgr = new Mock<IGroupManager>();
        hubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
        hubClients.Setup(c => c.Client(It.IsAny<string>())).Returns(new Mock<ISingleClientProxy>().Object);
        hubCtx.Setup(h => h.Clients).Returns(hubClients.Object);
        hubCtx.Setup(h => h.Groups).Returns(groupMgr.Object);

        var scopeFactory = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var manager = new GameManager(hubCtx.Object, scopeFactory.Object, new Mock<ILogger<GameManager>>().Object);
        _hub = new GameHub(manager, new Mock<ILogger<GameHub>>().Object);

        var ctx = new Mock<HubCallerContext>();
        ctx.Setup(c => c.ConnectionId).Returns("test-conn-1");
        typeof(Hub).GetProperty("Context")!.SetValue(_hub, ctx.Object);
    }

    // Why: CreateRoom is the first action any player takes; confirming it returns
    // a 6-character code and the Locksmith role proves the hub correctly delegates
    // to GameManager and maps the caller's connection to the new room.
    [Fact]
    public async Task CreateRoom_ReturnsSuccessWithRoomCode()
    {
        var result = await _hub.CreateRoom("Alice");

        Assert.True(result.Success);
        Assert.NotNull(result.RoomCode);
        Assert.Equal(6, result.RoomCode!.Length);
        Assert.Equal(PlayerRole.Locksmith, result.AssignedRole);
    }

    // Why: a typo or stale link should produce a clear error rather than an
    // unhandled exception; verifying the "not found" message ensures the client
    // can display a helpful UI prompt instead of crashing.
    [Fact]
    public async Task JoinRoom_InvalidCode_Fails()
    {
        var result = await _hub.JoinRoom("BADCODE", "Alice");

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }
}
