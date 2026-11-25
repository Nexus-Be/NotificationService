namespace Notification.Application.Services.WithdrawalEvents;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

/// <summary>Confirmed result of a withdrawal request, sent by Wallet after the wallet change is committed.</summary>
/// <remarks>
/// SourceEventId is a string chosen by Wallet (for example <c>withdrawal:{id}:Approved</c>) and stays the same when the call is retried.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WithdrawalEventRequest(
    [Required, StringLength(255)] string SourceEventId,
    Guid WithdrawalId,
    Guid RecipientUserId,
    [Required] string Status,
    DateTimeOffset ConfirmedAt,
    [StringLength(500)] string? DisplayReason,
    [StringLength(100)] string? TransactionId,
    [StringLength(100)] string? TraceId);

public sealed record WithdrawalEventResponse(string SourceEventId, bool IsReplay, Guid NotificationId);
