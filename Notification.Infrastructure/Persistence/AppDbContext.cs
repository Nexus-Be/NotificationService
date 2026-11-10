namespace Notification.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Notification.Application.Interfaces;
using Notification.Domain.Entities;

public class AppDbContext : DbContext, INotificationDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<PasswordChangeDispatch> PasswordChangeDispatches => Set<PasswordChangeDispatch>();
    public DbSet<SellerDecisionDispatch> SellerDecisionDispatches => Set<SellerDecisionDispatch>();
    public DbSet<AccountStatusChangeDispatch> AccountStatusChangeDispatches => Set<AccountStatusChangeDispatch>();
    public DbSet<OrderPaidDispatch> OrderPaidDispatches => Set<OrderPaidDispatch>();
    public DbSet<AuctionOutbidDispatch> AuctionOutbidDispatches => Set<AuctionOutbidDispatch>();
    public DbSet<AuctionWinnerDispatch> AuctionWinnerDispatches => Set<AuctionWinnerDispatch>();
    public DbSet<AuctionCancellationDispatch> AuctionCancellationDispatches => Set<AuctionCancellationDispatch>();
    public DbSet<AuctionCancellationRecipient> AuctionCancellationRecipients => Set<AuctionCancellationRecipient>();
    public DbSet<AuctionPaymentTimeoutDispatch> AuctionPaymentTimeoutDispatches => Set<AuctionPaymentTimeoutDispatch>();
    public DbSet<AuctionSellerResultDispatch> AuctionSellerResultDispatches => Set<AuctionSellerResultDispatch>();
    public DbSet<AuctionSellerPaymentTimeoutDispatch> AuctionSellerPaymentTimeoutDispatches => Set<AuctionSellerPaymentTimeoutDispatch>();
    public DbSet<AuctionStartDispatch> AuctionStartDispatches => Set<AuctionStartDispatch>();
    public DbSet<AuctionOrderPaidDispatch> AuctionOrderPaidDispatches => Set<AuctionOrderPaidDispatch>();
    public DbSet<OrderDecisionDispatch> OrderDecisionDispatches => Set<OrderDecisionDispatch>();
    public DbSet<OrderRefundDispatch> OrderRefundDispatches => Set<OrderRefundDispatch>();
    public DbSet<ShipmentNotificationStream> ShipmentNotificationStreams => Set<ShipmentNotificationStream>();
    public DbSet<ShipmentEventDispatch> ShipmentEventDispatches => Set<ShipmentEventDispatch>();
    public DbSet<OrderCompletionDispatch> OrderCompletionDispatches => Set<OrderCompletionDispatch>();
    public DbSet<OrderSettlementDispatch> OrderSettlementDispatches => Set<OrderSettlementDispatch>();
    public DbSet<OrderBuyerCancellationDispatch> OrderBuyerCancellationDispatches => Set<OrderBuyerCancellationDispatch>();
    public DbSet<OrderPaymentTimeoutDispatch> OrderPaymentTimeoutDispatches => Set<OrderPaymentTimeoutDispatch>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<DeliveryRetryRequest> DeliveryRetryRequests => Set<DeliveryRetryRequest>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<OtpEmailDispatch> OtpEmailDispatches => Set<OtpEmailDispatch>();
    public DbSet<EventLog> EventLogs => Set<EventLog>();
    public DbSet<BrokerHealthCheck> BrokerHealthChecks => Set<BrokerHealthCheck>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        Seeds.NotificationSeedData.SeedAll(modelBuilder);
    }
}
