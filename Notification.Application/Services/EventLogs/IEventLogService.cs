namespace Notification.Application.Services.EventLogs;

using Notification.Application.Common.Models;

public interface IEventLogService
{
    Task<EventLogAcceptanceResponse> CreateAsync(CreateEventLogRequest request, CancellationToken cancellationToken = default);
    Task<PagedData<EventLogSummaryResponse>> ListAsync(ListEventLogsRequest request, CancellationToken cancellationToken = default);
    Task<EventLogDetailResponse> GetAsync(Guid id, CancellationToken cancellationToken = default);
}
