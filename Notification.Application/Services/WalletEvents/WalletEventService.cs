namespace Notification.Application.Services.WalletEvents;

using System.Globalization;
using Notification.Application.Interfaces;
using Notification.Application.Services.ServiceEvents;
using Notification.Domain.Enums;
using Notification.Domain.Exceptions;

public sealed class WalletEventService(INotificationDbContext db, TimeProvider clock) : IWalletEventService
{
    public Task<ServiceEventResponse> CreateWithdrawalAsync(WithdrawalEventRequest r, CancellationToken ct = default)
    {
        if (r is null || !Text(r.SourceEventId, 200) || r.WithdrawalId == Guid.Empty || r.RecipientUserId == Guid.Empty ||
            r.Status is not ("Approved" or "Rejected" or "Completed" or "Failed") || r.ConfirmedAt == default ||
            !Optional(r.DisplayReason, 1000) || !Optional(r.TransactionId, 200) || !Optional(r.TraceId, 100)) throw Invalid();
        r = r with { ConfirmedAt = r.ConfirmedAt.ToUniversalTime() };
        var label = r.Status switch { "Approved" => "đã được duyệt", "Rejected" => "bị từ chối",
            "Completed" => "đã hoàn tất", _ => "không thành công" };
        var body = $"Yêu cầu rút tiền {r.WithdrawalId} {label}.";
        if (!string.IsNullOrWhiteSpace(r.DisplayReason)) body += $" Lý do: {r.DisplayReason}";
        return InAppEventWriter.CreateAsync(db, clock, "wallet", r.SourceEventId, r.RecipientUserId,
            NotificationType.WithdrawalResult, "Cập nhật yêu cầu rút tiền", body,
            new { withdrawalId = r.WithdrawalId, status = r.Status, confirmedAt = r.ConfirmedAt,
                displayReason = r.DisplayReason, transactionId = r.TransactionId }, r.TraceId, ct);
    }

    public Task<ServiceEventResponse> CreateDepositAsync(WalletDepositEventRequest r, CancellationToken ct = default)
    {
        if (r is null || !Text(r.SourceEventId, 200) || !Text(r.TransactionId, 200) || !Optional(r.AuctionId, 200) ||
            r.BidderId == Guid.Empty || r.Status is not ("Held" or "Released" or "Captured" or "Forfeited" or "Refunded") ||
            r.Amount <= 0 || r.Amount > 9999999999999999.99m || decimal.Round(r.Amount, 2) != r.Amount ||
            r.Currency != "VND" || r.ConfirmedAt == default || !Optional(r.TraceId, 100)) throw Invalid();
        r = r with { ConfirmedAt = r.ConfirmedAt.ToUniversalTime() };
        var label = r.Status switch { "Held" => "đã giữ", "Released" => "đã giải phóng", "Captured" => "đã thu",
            "Forfeited" => "đã tịch thu", _ => "đã hoàn" };
        return InAppEventWriter.CreateAsync(db, clock, "wallet", r.SourceEventId, r.BidderId,
            NotificationType.AuctionDeposit, "Cập nhật tiền cọc đấu giá",
            $"Tiền cọc {label}: {r.Amount.ToString(r.Amount == decimal.Truncate(r.Amount) ? "N0" : "N2", CultureInfo.GetCultureInfo("vi-VN"))} VND.",
            new { transactionId = r.TransactionId, auctionId = r.AuctionId, status = r.Status,
                amount = r.Amount, currency = r.Currency, confirmedAt = r.ConfirmedAt }, r.TraceId, ct);
    }

    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= max && value == value.Trim() && !value.Any(char.IsControl);
    private static bool Optional(string? value, int max) => value is null || value.Length <= max && !value.Any(char.IsControl);
    private static DomainException Invalid() => new("VAL_INVALID_INPUT", "Wallet notification fields are invalid.", 400);
}
