namespace Notification.Application.Services.Management;

using Notification.Application.Common.Models;

public interface INotificationManagementService
{
    Task<PagedData<TemplateResponse>> ListTemplatesAsync(TemplateListRequest request, CancellationToken cancellationToken = default);
    Task<TemplateResponse> GetTemplateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TemplateResponse> CreateTemplateAsync(CreateTemplateRequest request, ManagementActor actor, CancellationToken cancellationToken = default);
    Task<TemplateResponse> UpdateTemplateAsync(Guid id, UpdateTemplateRequest request, ManagementActor actor, CancellationToken cancellationToken = default);
    Task<PagedData<ChannelResponse>> ListChannelsAsync(ManagementPageRequest request, CancellationToken cancellationToken = default);
    Task<ChannelResponse> UpdateChannelAsync(string code, UpdateChannelRequest request, ManagementActor actor, CancellationToken cancellationToken = default);
    Task<PagedData<DeliveryResponse>> ListDeliveriesAsync(DeliveryListRequest request, CancellationToken cancellationToken = default);
    Task<DeliveryDetailResponse> GetDeliveryAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedData<DeliveryLogResponse>> ListLogsAsync(Guid id, ManagementPageRequest request, CancellationToken cancellationToken = default);
    Task<RetryAcceptanceResponse> RetryAsync(Guid id, RetryDeliveryRequest request, ManagementActor actor, CancellationToken cancellationToken = default);
}
