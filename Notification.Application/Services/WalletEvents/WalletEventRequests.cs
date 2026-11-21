namespace Notification.Application.Services.WalletEvents;

using System.Text.Json.Serialization;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WithdrawalEventRequest(string SourceEventId, Guid WithdrawalId, Guid RecipientUserId,
    string Status, DateTimeOffset ConfirmedAt, string? DisplayReason = null, string? TransactionId = null, string? TraceId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WalletDepositEventRequest(string SourceEventId, string TransactionId, string? AuctionId,
    Guid BidderId, string Status, decimal Amount, string Currency, DateTimeOffset ConfirmedAt, string? TraceId = null);
