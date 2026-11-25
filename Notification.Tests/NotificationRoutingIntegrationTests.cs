using Microsoft.Extensions.Configuration;
namespace Notification.Tests;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Notification.Application.Services.Routing;
using Notification.Application.Services.PasswordChanges;
using Notification.Application.Services.AccountStatusChanges;
using Notification.Application.Services.SellerDecisions;
using Notification.Application.Services.Dispatches;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.WalletEvents;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;
using Notification.Domain.Enums;
using Notification.Infrastructure.Persistence;
using Xunit;
public sealed class NotificationRoutingIntegrationTests : IAsyncLifetime
{
    private string? admin, database, connection;
    private AppDbContext Db(bool routing = true) {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection);
        if (routing) options.AddInterceptors(new NotificationRoutingInterceptor());
        return new(options.Options);
    }
    public async Task InitializeAsync() {
        admin=Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB"); if(string.IsNullOrWhiteSpace(admin)) return;
        database="routing_test_"+Guid.NewGuid().ToString("N");
        await using var c=new NpgsqlConnection(admin);await c.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE \"{database}\"",c).ExecuteNonQueryAsync();
        connection=new NpgsqlConnectionStringBuilder(admin){Database=database}.ConnectionString;
        await using var db=Db(false);await db.Database.MigrateAsync();
        await db.NotificationLogs.ExecuteDeleteAsync();await db.NotificationDeliveries.ExecuteDeleteAsync();
        await db.Notifications.ExecuteDeleteAsync();
    }
    public async Task DisposeAsync() {
        if(database is null)return;NpgsqlConnection.ClearAllPools();
        await using var c=new NpgsqlConnection(admin);await c.OpenAsync();
        await new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)",c).ExecuteNonQueryAsync();
    }
    [Theory]
    [InlineData(NotificationType.SecurityAlert,true)] [InlineData(NotificationType.OrderPaid,true)]
    [InlineData(NotificationType.WithdrawalResult,true)] [InlineData(NotificationType.AuctionWon,true)]
    [InlineData(NotificationType.AuctionOutbid,false)] [InlineData(NotificationType.AuctionStarted,false)]
    [InlineData(NotificationType.AuctionWatchlistBid,false)] [InlineData(NotificationType.ProductApproved,false)]
    [InlineData(NotificationType.Promotion,false)] [InlineData(NotificationType.Otp,false)]
    public void Matrix(NotificationType type,bool important)=>Assert.Equal(important,NotificationChannelPlanner.IsImportant(type));
    [Theory]
    [InlineData("Delivered",true)] [InlineData("DELIVERY_FAILED",true)] [InlineData("Returning",true)]
    [InlineData("InTransit",false)] [InlineData("Created",false)]
    public void ShipmentMatrix(string status,bool important)=>Assert.Equal(important,NotificationChannelPlanner.IsImportant(NotificationType.OrderShipped,"{\"status\":\""+status+"\"}"));
    [PostgresTheory]
    [InlineData("changed")] [InlineData("reset")]
    public async Task Password_EmailAndInboxAtomic_ReplayKeepsOriginalEmailReceipt(string kind) {
        var r=new CreatePasswordChangeRequest{SourceEventId=Guid.NewGuid(),UserId=Guid.NewGuid(),Email="receiver@example.com",FullName="<script>x</script>",Kind=kind,ChangedAt=DateTimeOffset.UtcNow};
        await using var db=Db();var svc=new PasswordChangeService(db,new DispatchService(db),TimeProvider.System);
        var first=await svc.CreateAsync(r);var replay=await svc.CreateAsync(r);
        Assert.True(replay.IsReplay);Assert.Equal(first.DeliveryId,replay.DeliveryId);
        var row=await db.Notifications.SingleAsync(n=>n.Id==first.NotificationId);
        Assert.True(row.IsInboxVisible);Assert.False(row.IsRead);Assert.DoesNotContain("<p>",row.Body!);
        var deliveries=await db.NotificationDeliveries.Where(d=>d.NotificationId==row.Id).ToListAsync();
        Assert.Equal(2,deliveries.Count);Assert.Single(deliveries,d=>d.ChannelCode==ChannelType.InApp&&d.Status==DeliveryStatus.Sent);
        var email=Assert.Single(deliveries,d=>d.ChannelCode==ChannelType.Email);Assert.Equal(first.DeliveryId,email.Id);
        db.ChangeTracker.Clear();var sender=new Sender();Assert.True(await new DeliveryService(db,sender,new(),TimeProvider.System).ProcessNextAsync());
        Assert.Single(sender.Messages);Assert.Contains("&lt;script&gt;",sender.Messages[0].HtmlBody);Assert.DoesNotContain("<script>",sender.Messages[0].HtmlBody);
    }
    [PostgresTheory]
    [InlineData("banned")] [InlineData("unbanned")]
    public async Task AccountStatus_ReasonInBothChannels_ReplayWorks(string change) {
        await using var db=Db();var svc=new AccountStatusChangeService(db,new DispatchService(db),TimeProvider.System);
        var r=new CreateAccountStatusChangeRequest{SourceEventId=Guid.NewGuid(),UserId=Guid.NewGuid(),Email="test@example.com",StatusVersion=1,Change=change,Reason="QA reason <unsafe>",ChangedAt=DateTimeOffset.UtcNow};
        var first=await svc.CreateAsync(r);var replay=await svc.CreateAsync(r);Assert.True(replay.IsReplay);Assert.Equal(first.DeliveryId,replay.DeliveryId);
        var row=await db.Notifications.SingleAsync(n=>n.Id==first.NotificationId);Assert.True(row.IsInboxVisible);Assert.Contains("QA reason",row.Body!);
        Assert.Equal(2,await db.NotificationDeliveries.CountAsync(d=>d.NotificationId==row.Id));
    }
    [PostgresTheory]
    [InlineData("approved")] [InlineData("rejected")]
    public async Task Seller_BothChannels_ReplayWorks(string decision) {
        await using var db=Db();var svc=new SellerDecisionService(db,new DispatchService(db),TimeProvider.System);
        var r=new CreateSellerDecisionRequest{SourceEventId=Guid.NewGuid(),ApplicationId=Guid.NewGuid(),UserId=Guid.NewGuid(),Email="test@example.com",ShopName="QA shop",Decision=decision,RejectionReason=decision=="rejected"?"QA reason":null,DecidedAt=DateTimeOffset.UtcNow};
        var first=await svc.CreateAsync(r);var replay=await svc.CreateAsync(r);Assert.True(replay.IsReplay);Assert.Equal(first.DeliveryId,replay.DeliveryId);
        Assert.True((await db.Notifications.SingleAsync(n=>n.Id==first.NotificationId)).IsInboxVisible);
        Assert.Equal(2,await db.NotificationDeliveries.CountAsync(d=>d.NotificationId==first.NotificationId));
    }
    [PostgresFact]
    public async Task Wallet_TemplateLessEmail_UsesVerifiedRecipient_AndRetriesWithoutDuplicate() {
        await using var db=Db();var svc=new WalletEventService(db,TimeProvider.System);
        var r=new WithdrawalEventRequest("qa-"+Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Approved",DateTimeOffset.UtcNow,"QA reason",null,null);
        var first=await svc.CreateWithdrawalAsync(r);var replay=await svc.CreateWithdrawalAsync(r);Assert.True(replay.IsReplay);
        Assert.Equal(2,await db.NotificationDeliveries.CountAsync(d=>d.NotificationId==first.NotificationId));
        db.ChangeTracker.Clear();var sender=new Sender();var resolver=new Resolver();Assert.True(await new DeliveryService(db,sender,new(),TimeProvider.System,recipients:resolver).ProcessNextAsync());
        Assert.Equal(r.RecipientUserId,resolver.User);Assert.Equal("verified@example.com",Assert.Single(sender.Messages).Recipient);
    }
    [PostgresFact]
    public async Task Ordinary_InAppOnly_NoQueuedEmailOrPush_HistoricalRowsUnchanged() {
        await using var db=Db();var row=Row(NotificationType.AuctionOutbid);db.Notifications.Add(row);
        db.NotificationDeliveries.Add(Delivery(row,ChannelType.Push));await db.SaveChangesAsync();
        var list=await db.NotificationDeliveries.Where(d=>d.NotificationId==row.Id).ToListAsync();
        Assert.Equal(2,list.Count);Assert.Single(list,d=>d.ChannelCode==ChannelType.InApp&&d.Status==DeliveryStatus.Sent);
        Assert.Single(list,d=>d.ChannelCode==ChannelType.Push&&d.ErrorCode=="IN_APP_ONLY");
        row.Title="Updated";await db.SaveChangesAsync();Assert.Equal(2,await db.NotificationDeliveries.CountAsync());
    }
    [PostgresFact]
    public async Task EmailFailureDoesNotHideInbox_OrDuplicateAcceptance() {
        await using var db=Db();var row=Row(NotificationType.OrderPaid);db.Notifications.Add(row);await db.SaveChangesAsync();
        db.ChangeTracker.Clear();var sender=new Sender{Fail=true};Assert.True(await new DeliveryService(db,sender,new(),TimeProvider.System,recipients:new Resolver()).ProcessNextAsync());
        var stored=await db.Notifications.AsNoTracking().SingleAsync(n=>n.Id==row.Id);Assert.True(stored.IsInboxVisible);Assert.False(stored.IsDeleted);
        Assert.Equal(2,await db.NotificationDeliveries.CountAsync());
        Assert.Equal(DeliveryStatus.Failed,(await db.NotificationDeliveries.AsNoTracking().SingleAsync(d=>d.ChannelCode==ChannelType.Email)).Status);
    }
    [PostgresFact]
    public async Task Rollback_LeavesNeitherInboxNorEmail() {
        await using(var db=Db()){await using var tx=await db.Database.BeginTransactionAsync();db.Notifications.Add(Row(NotificationType.OrderPaid));await db.SaveChangesAsync();await tx.RollbackAsync();}
        await using var read=Db();Assert.Equal(0,await read.Notifications.CountAsync());Assert.Equal(0,await read.NotificationDeliveries.CountAsync());
    }

    [PostgresFact]
    public async Task EntireTypeMatrixPlansChannels_ForNewRecordsOnly() {
        await using var db=Db();
        var rows=Enum.GetValues<NotificationType>().Select(type=>Row(type)).ToList();
        rows.Single(r=>r.NotificationType==NotificationType.OrderShipped).Payload="{\"status\":\"Delivered\"}";
        db.Notifications.AddRange(rows);await db.SaveChangesAsync();
        var deliveries=await db.NotificationDeliveries.AsNoTracking().ToListAsync();
        foreach(var row in rows){
            var own=deliveries.Where(d=>d.NotificationId==row.Id).ToList();
            if(row.NotificationType==NotificationType.Otp){Assert.Empty(own);continue;}
            Assert.Single(own,d=>d.ChannelCode==ChannelType.InApp&&d.Status==DeliveryStatus.Sent);
            Assert.Equal(NotificationChannelPlanner.IsImportant(row.NotificationType!.Value,row.Payload)?1:0,own.Count(d=>d.ChannelCode==ChannelType.Email));
            foreach(var email in own.Where(d=>d.ChannelCode==ChannelType.Email)){Assert.True(email.BodyOverride!.Length<=4000);Assert.Contains("NEXUS",email.BodyOverride);Assert.DoesNotContain("<title>",email.BodyOverride);}
        }
    }
    [PostgresFact]
    public async Task ProductionCompositionRegistersPlannerByDefault(){
        var config=new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["ConnectionStrings:DefaultConnection"]=connection,["DeliveryWorker:Enabled"]="false",["OtpWorker:Enabled"]="false"
        }).Build();
        var services=new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
        Notification.Infrastructure.DependencyInjection.AddInfrastructureServices(services,config);
        using var provider=Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        using var scope=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(provider);
        var db=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<AppDbContext>(scope.ServiceProvider);
        var row=Row(NotificationType.OrderPaid);db.Notifications.Add(row);await db.SaveChangesAsync();
        Assert.Equal(2,await db.NotificationDeliveries.CountAsync(d=>d.NotificationId==row.Id));
    }
    [PostgresFact]
    public async Task DisabledEmailChannelDoesNotBlockInbox(){
        await using var db=Db();await db.NotificationChannels.Where(c=>c.Code==ChannelType.Email).ExecuteUpdateAsync(s=>s.SetProperty(c=>c.IsDisabled,true));
        var row=Row(NotificationType.OrderPaid);db.Notifications.Add(row);await db.SaveChangesAsync();
        Assert.True(row.IsInboxVisible);Assert.Equal(DeliveryStatus.Skipped,(await db.NotificationDeliveries.SingleAsync(d=>d.ChannelCode==ChannelType.Email)).Status);
        Assert.False(await new DeliveryService(db,new Sender(),new(),TimeProvider.System).ProcessNextAsync());
    }
    [PostgresFact]
    public async Task PlainTextIsNotParsedAsHtml(){
        await using var db=Db();var row=Row(NotificationType.AuctionOutbid);row.Body="Giá A < B > C và cửa hàng <Nexus>";
        db.Notifications.Add(row);await db.SaveChangesAsync();
        Assert.Equal("Giá A < B > C và cửa hàng <Nexus>",(await db.Notifications.AsNoTracking().SingleAsync(n=>n.Id==row.Id)).Body);
    }
    private static NotificationRecord Row(NotificationType type)=>new(){Id=Guid.NewGuid(),UserId=Guid.NewGuid(),NotificationType=type,Title="QA <title>",Body="QA body",Status=NotificationStatus.Completed,CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow};
    private static NotificationDelivery Delivery(NotificationRecord n,ChannelType channel)=>new(){Id=Guid.NewGuid(),NotificationId=n.Id,ChannelCode=channel,Destination=Guid.NewGuid().ToString(),Status=DeliveryStatus.Pending,CreatedAt=n.CreatedAt,UpdatedAt=n.CreatedAt};
    private sealed class Resolver:INotificationRecipientResolver{public Guid User;public Task<string?> ResolveAsync(Guid userId,CancellationToken ct=default){User=userId;return Task.FromResult<string?>("verified@example.com");}}
    private sealed class Sender:IEmailDeliverySender{
        public bool Fail;public List<EmailDeliveryMessage> Messages=new();
        public Task<EmailDeliveryResult> SendAsync(EmailDeliveryMessage message,CancellationToken cancellationToken=default){Messages.Add(message);return Task.FromResult(Fail?EmailDeliveryResult.Failure("SMTP_AUTH_FAILED","Authentication failed."):EmailDeliveryResult.Sent());}
    }
}
