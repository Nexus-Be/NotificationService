namespace Notification.Domain.Enums;

public enum AuditAction
{
    ChannelCreate = 0,
    ChannelUpdate = 1,
    TemplateCreate = 2,
    TemplateUpdate = 3,
    PreferenceUpdate = 4,
    DeviceRegister = 5,
    DeviceRevoke = 6,
    DeliveryRetry = 7,
    OtpAccepted = 8,
    OtpAttempt = 9,
    OtpResult = 10,
    PasswordChangeAccepted = 11,
    SellerDecisionAccepted = 12,
    AccountStatusChangeAccepted = 13,
    OrderPaymentAccepted = 14,
    AuctionOutbidAccepted = 15,
    AuctionWinnerAccepted = 16
}
