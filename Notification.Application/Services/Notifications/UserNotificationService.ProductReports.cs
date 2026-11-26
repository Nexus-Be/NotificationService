namespace Notification.Application.Services.Notifications;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Notification.Application.Services.ServiceEvents;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed partial class UserNotificationService
{
    private static NotificationType? ProductReportType(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "PRODUCT_BANNED_REPORT" or "PRODUCTBANNEDREPORT" or "PRODUCT_BANNED_BY_REPORT" or "PRODUCTBANNEDBYREPORT" => NotificationType.ProductBannedByReport,
        "REPORT_RESOLVED" or "PRODUCTREPORTRESOLVED" => NotificationType.ProductReportResolved,
        "REPORT_DISMISSED" or "PRODUCTREPORTDISMISSED" => NotificationType.ProductReportDismissed,
        "PRODUCT_APPROVED" or "PRODUCTAPPROVED" => NotificationType.ProductApproved,
        "PRODUCT_REJECTED" or "PRODUCTREJECTED" => NotificationType.ProductRejected,
        "PRODUCT_SUSPENDED" or "PRODUCTSUSPENDED" => NotificationType.ProductSuspended,
        "PRODUCT_BANNED" or "PRODUCTBANNED" => NotificationType.ProductBanned,
        _ => null
    };

    private async Task<InAppNotificationItemResponse?> CreateProductReportAsync(CreateInAppNotificationRequest request,
        NotificationType type, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Payload) || request.Payload.Length > 16384) throw InvalidProductReport();
        var isReport = type is NotificationType.ProductBannedByReport or NotificationType.ProductReportResolved or NotificationType.ProductReportDismissed;
        if (request.SourceEventId == Guid.Empty || !isReport && request.SourceEventId is null) throw InvalidProductReport();
        Guid product;
        Guid? report = null;
        try
        {
            using var json = JsonDocument.Parse(request.Payload);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("productId", out var p) || p.ValueKind != JsonValueKind.String || !p.TryGetGuid(out product) || product == Guid.Empty)
                throw InvalidProductReport();
            if (isReport)
            {
                if (!json.RootElement.TryGetProperty("reportId", out var r) || r.ValueKind != JsonValueKind.String || !r.TryGetGuid(out var reportId) || reportId == Guid.Empty)
                    throw InvalidProductReport();
                report = reportId;
            }
        }
        catch (JsonException) { throw InvalidProductReport(); }

        // The source currently declares a fixed penalty that differs from the applied reputation change.
        // Do not repeat it or any unreviewed admin note in the recipient-facing snapshot.
        var (title, body) = type switch
        {
            NotificationType.ProductApproved => ("Sản phẩm đã được duyệt", "Sản phẩm của bạn đã được duyệt mở bán. Vui lòng kiểm tra trạng thái sản phẩm hiện tại."),
            NotificationType.ProductRejected => ("Sản phẩm chưa được duyệt", "Sản phẩm của bạn chưa đạt yêu cầu kiểm duyệt. Vui lòng xem chi tiết và cập nhật thông tin."),
            NotificationType.ProductSuspended => ("Sản phẩm bị tạm ẩn", "Sản phẩm của bạn đã bị tạm ẩn. Vui lòng xem quyết định kiểm duyệt để biết hướng xử lý."),
            NotificationType.ProductBanned => ("Sản phẩm bị cấm", "Sản phẩm của bạn bị cấm do vi phạm. Vui lòng xem quyết định kiểm duyệt."),
            NotificationType.ProductBannedByReport => ("Sản phẩm bị cấm do vi phạm",
                "Sản phẩm của bạn bị cấm sau khi báo cáo vi phạm được xử lý. Vui lòng xem chi tiết sản phẩm và quyết định kiểm duyệt."),
            NotificationType.ProductReportResolved => ("Báo cáo sản phẩm đã được xử lý",
                "Báo cáo sản phẩm của bạn đã được xác nhận và xử lý. Cảm ơn bạn đã hỗ trợ bảo vệ cộng đồng."),
            _ => ("Kết quả báo cáo sản phẩm", "Báo cáo sản phẩm của bạn đã được xem xét và bác bỏ. Vui lòng xem kết quả xử lý.")
        };
        var eventId = request.SourceEventId is { } supplied && supplied != Guid.Empty ? supplied.ToString("D") : $"report:{report:D}:{type}:{request.UserId:D}";
        var result = await InAppEventWriter.CreateAsync(_db, _time, "catalog", eventId, request.UserId,
            type, title, body, new { productId = product, reportId = report }, null, ct);
        var row = await _db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleAsync(n => n.Id == result.NotificationId, ct);
        return !row.IsInboxVisible || row.IsDeleted ? null : new(row.Id, row.UserId, row.Title, row.Body, row.Payload,
            row.IsRead, row.ReadAt, row.NotificationType?.ToString(), row.Status.ToString(), row.CreatedAt);
    }

    private static DomainException InvalidProductReport() => new("VAL_INVALID_INPUT", "Product report identifiers are invalid.", 400);
}
