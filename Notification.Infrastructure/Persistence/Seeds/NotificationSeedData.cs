namespace Notification.Infrastructure.Persistence.Seeds;

using Microsoft.EntityFrameworkCore;
using Notification.Domain.Entities;
using Notification.Domain.Enums;

public static class NotificationSeedData
{
    public static void SeedAll(ModelBuilder modelBuilder)
    {
        var seedDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var buyerUserId = Guid.Parse("dddddddd-4444-4444-4444-444444444444");

        // 1. Notification Channels
        modelBuilder.Entity<NotificationChannel>().HasData(
            new NotificationChannel
            {
                Id = Guid.Parse("10101010-1010-1010-1010-101010101010"),
                Code = ChannelType.Email,
                Name = "Thư điện tử (Email)",
                Provider = ChannelProvider.Smtp,
                IsDisabled = false,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new NotificationChannel
            {
                Id = Guid.Parse("20202020-2020-2020-2020-202020202020"),
                Code = ChannelType.Push,
                Name = "Thông báo đẩy di động (Push)",
                Provider = ChannelProvider.Fcm,
                IsDisabled = false,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new NotificationChannel
            {
                Id = Guid.Parse("30303030-3030-3030-3030-303030303030"),
                Code = ChannelType.InApp,
                Name = "Thông báo trung tâm ứng dụng (InApp)",
                IsDisabled = false,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new NotificationChannel
            {
                Id = Guid.Parse("40404040-4040-4040-4040-404040404040"),
                Code = ChannelType.Sms,
                Name = "Tin nhắn viễn thông (SMS)",
                IsDisabled = false,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );

        // 2. Notification Templates
        var templateOrderPaidId = Guid.Parse("60000000-0000-0000-0000-000000000001");
        var templateOrderShippedId = Guid.Parse("60000000-0000-0000-0000-000000000002");

        modelBuilder.Entity<NotificationTemplate>().HasData(
            new NotificationTemplate
            {
                Id = templateOrderPaidId,
                Code = "ORDER_PAID_EMAIL",
                NotificationType = NotificationType.OrderPaid,
                ChannelCode = ChannelType.Email,
                SubjectTemplate = "Xác nhận thanh toán đơn hàng {{OrderNo}}",
                BodyTemplate = "Đơn hàng {{OrderNo}} của bạn đã được thanh toán thành công số tiền {{TotalAmount}}.",
                IsMandatory = false,
                Locale = "vi-VN",
                IsActive = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new NotificationTemplate
            {
                Id = templateOrderShippedId,
                Code = "ORDER_SHIPPED_EMAIL",
                NotificationType = NotificationType.OrderShipped,
                ChannelCode = ChannelType.Email,
                SubjectTemplate = "Đơn hàng {{OrderNo}} đang trên đường giao đến bạn",
                BodyTemplate = "Đơn hàng {{OrderNo}} của bạn đã được đóng gói và bàn giao cho đơn vị vận chuyển.",
                IsMandatory = false,
                Locale = "vi-VN",
                IsActive = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );

        // 3. Notification Record (Linked to Buyer User and SubOrder)
        modelBuilder.Entity<NotificationRecord>().HasData(
            new NotificationRecord
            {
                Id = Guid.Parse("61000000-0000-0000-0000-000000000001"),
                UserId = buyerUserId,
                TemplateId = templateOrderPaidId,
                NotificationType = NotificationType.OrderPaid,
                ChannelScope = ChannelScope.All,
                Title = "Thanh toán thành công đơn hàng SUB-20260101-0001",
                Body = "Đơn hàng SUB-20260101-0001 với số tiền 850.000 đ đã được thanh toán thành công qua Ví Nexus.",
                Payload = "{\"subOrderId\":\"02000000-0000-0000-0000-000000000001\",\"amount\":850000}",
                IsRead = true,
                ReadAt = seedDate.AddMinutes(5),
                Status = NotificationStatus.Completed,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );
    }
}
