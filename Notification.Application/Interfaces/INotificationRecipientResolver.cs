namespace Notification.Application.Interfaces;
public interface INotificationRecipientResolver
{
    Task<string?> ResolveAsync(Guid userId, CancellationToken ct = default);
}
