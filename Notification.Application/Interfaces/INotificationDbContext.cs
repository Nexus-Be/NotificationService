namespace Notification.Application.Interfaces;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Notification.Domain.Entities;

public interface INotificationDbContext
{
    DbSet<PasswordChangeDispatch> PasswordChangeDispatches { get; }
    DbSet<SellerDecisionDispatch> SellerDecisionDispatches { get; }
    DbSet<AccountStatusChangeDispatch> AccountStatusChangeDispatches { get; }
    DbSet<OrderPaidDispatch> OrderPaidDispatches { get; }
    DbSet<AuctionOutbidDispatch> AuctionOutbidDispatches { get; }
    DbSet<AuctionWinnerDispatch> AuctionWinnerDispatches { get; }
    DatabaseFacade Database { get; }
    DbSet<NotificationChannel> NotificationChannels { get; }
    DbSet<NotificationTemplate> NotificationTemplates { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<DeviceToken> DeviceTokens { get; }
    DbSet<NotificationRecord> Notifications { get; }
    DbSet<NotificationDelivery> NotificationDeliveries { get; }
    DbSet<DeliveryRetryRequest> DeliveryRetryRequests { get; }
    DbSet<NotificationLog> NotificationLogs { get; }
    DbSet<OtpEmailDispatch> OtpEmailDispatches { get; }
    DbSet<EventLog> EventLogs { get; }
    DbSet<BrokerHealthCheck> BrokerHealthChecks { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
