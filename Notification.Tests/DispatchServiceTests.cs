namespace Notification.Tests;

using Microsoft.EntityFrameworkCore;
using Notification.Application.Services.Dispatches;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;
using Notification.Infrastructure.Persistence;
using Xunit;

public sealed class DispatchServiceTests
{
    private static async Task<AppDbContext> CreateDb()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    internal static CreateDispatchRequest Request(string template = "ORDER_PAID_IN_APP") => new()
    {
        SourceEventId = Guid.NewGuid(), UserId = Guid.NewGuid(), TemplateCode = template,
        Destination = "user@example.com", Data = new() { ["OrderNo"] = "NEX-001", ["TotalAmount"] = "150000 VND" }
    };

    [Theory]
    [InlineData(true, true, "Sent")]
    [InlineData(false, true, "Skipped")]
    [InlineData(false, false, "Sent")]
    public async Task MandatoryAndPreferences_AreApplied(bool mandatory, bool disabled, string expected)
    {
        await using var db = await CreateDb();
        var template = await db.NotificationTemplates.SingleAsync(t => t.Code == "ORDER_PAID_IN_APP");
        template.IsMandatory = mandatory;
        template.NotificationType = NotificationType.OrderShipped; // Optional type; critical types cannot be disabled.
        var request = Request();
        db.NotificationPreferences.Add(new NotificationPreference { Id = Guid.NewGuid(), UserId = request.UserId!.Value,
            NotificationType = NotificationType.OrderShipped, ChannelCode = ChannelType.InApp, IsDisabled = disabled });
        await db.SaveChangesAsync();
        var result = await new DispatchService(db).CreateAsync(request);
        Assert.Equal(expected, result.DeliveryStatus);
        var record = await db.Notifications.SingleAsync(n => n.Id == result.NotificationId);
        Assert.Contains("NEX-001", record.Body);
        Assert.Equal(expected == "Skipped", record.IsRead);
        var history = await new Notification.Application.Services.Notifications.UserNotificationService(db).GetMyNotificationsAsync(request.UserId!.Value, new Notification.Application.Services.Notifications.GetMyNotificationsRequest());
        Assert.Equal(expected != "Skipped", history.Items.Any(n => n.Id == record.Id));
        Assert.Single(db.NotificationLogs.Where(l => l.NotificationId == result.NotificationId));
    }

    [Fact]
    public async Task DisabledChannel_OverridesMandatory()
    {
        await using var db = await CreateDb();
        (await db.NotificationChannels.SingleAsync(c => c.Code == ChannelType.InApp)).IsDisabled = true;
        await db.SaveChangesAsync();
        var result = await new DispatchService(db).CreateAsync(Request());
        Assert.Equal("Skipped", result.DeliveryStatus);
        Assert.Equal(0, (await db.NotificationDeliveries.SingleAsync(d => d.Id == result.DeliveryId)).AttemptCount);
    }

    [Fact]
    public async Task Replay_ReturnsOriginalDecision_AfterPreferenceChangeAndSoftDelete()
    {
        await using var db = await CreateDb();
        var request = Request();
        var service = new DispatchService(db);
        var first = await service.CreateAsync(request);
        (await db.Notifications.SingleAsync(n => n.Id == first.NotificationId)).IsDeleted = true;
        await db.SaveChangesAsync();
        var replay = await service.CreateAsync(request with { Data = new() });
        Assert.True(replay.IsReplay);
        Assert.Equal(first.NotificationId, replay.NotificationId);
        Assert.Equal(first.DeliveryId, replay.DeliveryId);
        Assert.Single(db.Notifications.Where(n => n.SourceEventId == request.SourceEventId));
    }

    [Fact]
    public async Task Email_IsPending_AndHtmlValuesAreEncoded()
    {
        await using var db = await CreateDb();
        var request = Request("ORDER_PAID_EMAIL");
        request.Data["OrderNo"] = "<script>x</script>";
        var result = await new DispatchService(db).CreateAsync(request);
        Assert.Equal("Pending", result.DeliveryStatus);
        Assert.Equal("Pending", result.NotificationStatus);
        var record = await db.Notifications.SingleAsync(n => n.Id == result.NotificationId);
        Assert.Contains("&lt;script&gt;", record.Body);
        var delivery = await db.NotificationDeliveries.SingleAsync(d => d.Id == result.DeliveryId);
        Assert.Equal(0, delivery.AttemptCount);
        Assert.Null(delivery.LastAttemptAt);
    }

    [Fact]
    public async Task EmailAndInApp_UseSeparateTemplateIdempotencyKeys()
    {
        await using var db = await CreateDb();
        var service = new DispatchService(db);
        var request = Request();
        var first = await service.CreateAsync(request);
        var second = await service.CreateAsync(request with { TemplateCode = "ORDER_PAID_EMAIL" });
        Assert.NotEqual(first.NotificationId, second.NotificationId);
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.SourceEventId == request.SourceEventId));
    }

    [Theory]
    [InlineData("missing", "TEMPLATE_NOT_FOUND")]
    [InlineData("inactive", "TEMPLATE_NOT_FOUND")]
    [InlineData("deleted", "TEMPLATE_NOT_FOUND")]
    [InlineData("channel", "CHANNEL_NOT_FOUND")]
    [InlineData("sms", "CHANNEL_NOT_SUPPORTED")]
    [InlineData("data", "TEMPLATE_DATA_MISSING")]
    [InlineData("empty-id", "VAL_INVALID_INPUT")]
    [InlineData("destination", "VAL_INVALID_INPUT")]
    public async Task InvalidDispatch_DoesNotPersist(string scenario, string expected)
    {
        await using var db = await CreateDb();
        var request = Request();
        var template = await db.NotificationTemplates.SingleAsync(t => t.Code == request.TemplateCode);
        switch (scenario)
        {
            case "missing": request = request with { TemplateCode = "MISSING" }; break;
            case "inactive": template.IsActive = false; break;
            case "deleted": template.IsDeleted = true; break;
            case "channel": (await db.NotificationChannels.SingleAsync(c => c.Code == ChannelType.InApp)).IsDeleted = true; break;
            case "sms": template.ChannelCode = ChannelType.Sms; break;
            case "data": request.Data.Remove("OrderNo"); break;
            case "empty-id": request = request with { SourceEventId = Guid.Empty }; break;
            case "destination": request = request with { TemplateCode = "ORDER_PAID_EMAIL", Destination = null }; break;
        }
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<DomainException>(() => new DispatchService(db).CreateAsync(request));
        Assert.Equal(expected, error.ErrorCode);
        Assert.Empty(db.Notifications.Where(n => n.SourceEventId == request.SourceEventId));
    }

    [Fact]
    public void Renderer_DoesNotEvaluateSubstitutedPlaceholders()
    {
        var result = TemplateRenderer.Render("Hello {{Name}}", new Dictionary<string,string> { ["Name"] = "{{Secret}}", ["Secret"] = "hidden" }, false, 500);
        Assert.Equal("Hello {{Secret}}", result);
    }

    [Theory]
    [InlineData("Bad {{Name", 500)]
    [InlineData("{{Name}}", 1)]
    [InlineData("", 500)]
    public void Renderer_RejectsMalformedOrOversizedContent(string template, int max)
    {
        var ex = Assert.Throws<DomainException>(() => TemplateRenderer.Render(template, new Dictionary<string,string> { ["Name"] = "Alice" }, false, max));
        Assert.Equal("TEMPLATE_INVALID", ex.ErrorCode);
    }
}
