namespace Notification.Api.Services;

using System.Globalization;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Notification.Application.Services.Notifications;
using ShoppingCart.Contracts.V1;
using NotificationMessage = ShoppingCart.Contracts.V1.Notification;

[Authorize]
public sealed class NotificationGrpcService : NotificationApi.NotificationApiBase
{
    private readonly IUserNotificationService _service;

    public NotificationGrpcService(IUserNotificationService service)
    {
        _service = service;
    }

    public override async Task<NotificationPage> ListNotifications(
        ListNotificationsRequest request,
        ServerCallContext context)
    {
        var userId = GetUserId(context);

        try
        {
            var page = await _service.ListAsync(
                userId,
                request.PageIndex,
                request.PageSize,
                context.CancellationToken);

            var response = new NotificationPage { Total = page.Total };
            response.Items.AddRange(page.Items.Select(ToMessage));
            return response;
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    public override async Task<NotificationMessage> MarkNotificationRead(
        MarkNotificationReadRequest request,
        ServerCallContext context)
    {
        var userId = GetUserId(context);

        if (!Guid.TryParse(request.Id, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Notification ID must be a valid non-empty UUID."));
        }

        var notification = await _service.MarkAsReadAsync(
            userId,
            id,
            context.CancellationToken);

        if (notification is null)
        {
            throw new RpcException(new Status(
                StatusCode.NotFound,
                "Notification not found or does not belong to the user."));
        }

        return ToMessage(notification);
    }

    private static Guid GetUserId(ServerCallContext context)
    {
        var user = context.GetHttpContext().User;
        var subject = user.FindFirst("sub")?.Value;

        if (user.Identity?.IsAuthenticated != true
            || !Guid.TryParse(subject, out var userId)
            || userId == Guid.Empty)
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "A valid authenticated user ID is required."));
        }

        return userId;
    }

    private static NotificationMessage ToMessage(NotificationDto dto)
    {
        var message = new NotificationMessage
        {
            Id = dto.Id.ToString(),
            Title = dto.Title ?? "",
            Body = dto.Body ?? "",
            IsRead = dto.IsRead,
            CreatedAt = dto.CreatedAt.ToString(
                "O", CultureInfo.InvariantCulture)
        };

        if (dto.ReadAt.HasValue)
        {
            message.ReadAt = dto.ReadAt.Value.ToString(
                "O", CultureInfo.InvariantCulture);
        }

        return message;
    }
}