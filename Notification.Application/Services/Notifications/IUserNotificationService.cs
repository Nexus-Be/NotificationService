namespace Notification.Application.Services.Notifications;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IUserNotificationService
{
    Task<NotificationPageDto> ListAsync(
        Guid userId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default
    );

    Task<NotificationDto?> MarkAsReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default
    );

    Task<DeviceRegisteredResponse> RegisterDeviceAsync(
        Guid userId,
        RegisterDeviceRequest request,
        CancellationToken cancellationToken = default
    );

    Task RevokeDeviceAsync(
        string token,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<NotificationPreferenceResponse>> GetPreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    );

    Task<UpdatePreferencesResult> UpdatePreferencesAsync(
        Guid userId,
        UpdatePreferencesRequest request,
        CancellationToken cancellationToken = default
    );

    Task<PagedInAppNotificationResponse> GetMyNotificationsAsync(
        Guid userId,
        GetMyNotificationsRequest request,
        CancellationToken cancellationToken = default
    );

    Task<BatchMarkReadResult> MarkNotificationsAsReadAsync(
        Guid userId,
        BatchMarkReadRequest request,
        CancellationToken cancellationToken = default
    );

    Task<PagedInAppNotificationResponse> SearchNotificationHistoryAsync(
        SearchNotificationHistoryRequest request,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<BrokerHealthCheckResponse>> GetBrokerHealthAsync(
        int limit = 50,
        CancellationToken cancellationToken = default
    );
}
