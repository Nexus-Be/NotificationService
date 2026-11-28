namespace Notification.Tests;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Api.Controllers.Public;
using Notification.Api.Hubs;
using Notification.Application.Services.AuctionOutbids;
using Xunit;

public sealed class AuctionOutbidRealtimeTests
{
    [Theory]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 0)]
    public async Task PublishesOnlyNewVisibleOutbid_ToRecipientUser(bool replay, bool visible, int expected)
    {
        var userId = Guid.NewGuid();
        var response = new AuctionOutbidResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            userId, "Completed", visible, replay, []);
        var service = new Mock<IAuctionOutbidService>();
        service.Setup(s => s.CreateAsync(It.IsAny<CreateAuctionOutbidRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var proxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.User(userId.ToString())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var controller = new AuctionOutbidsController(service.Object, hub.Object,
            NullLogger<AuctionOutbidsController>.Instance);

        var result = await controller.Create(new CreateAuctionOutbidRequest(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        clients.Verify(c => c.User(userId.ToString()), Times.Exactly(expected));
        proxy.Verify(p => p.SendCoreAsync("NotificationCreated", It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Exactly(expected));
    }
}
