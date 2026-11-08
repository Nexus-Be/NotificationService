namespace Notification.Domain.Enums;

public enum OtpDispatchStatus { Queued = 0, Sending = 1, ProviderAccepted = 2, Failed = 3, Expired = 4, Superseded = 5 }
