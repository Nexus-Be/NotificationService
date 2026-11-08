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
                Id = Guid.Parse("60000000-0000-0000-0000-00000000000f"), Code = "AUCTION_WINNER_EMAIL", NotificationType = NotificationType.AuctionWon, ChannelCode = ChannelType.Email,
                SubjectTemplate = "Kết quả thắng đấu giá và hướng dẫn thanh toán",
                BodyTemplate = "<p>Xin chào {{FullName}},</p><p>Bạn đã thắng phiên {{AuctionTitle}}, kết quả chốt lúc {{SettledAtUtc}}.</p><p>Giá thắng: {{WinningAmount}} VND. Hạn thanh toán: {{PaymentDeadlineUtc}}.</p><p>Tham chiếu đơn: {{OrderReference}}</p><p>Hãy mở chi tiết phiên và đơn đấu giá liên quan trong ứng dụng Nexus. Kiểm tra số tiền cuối cùng và chỉ thanh toán khi hệ thống xác nhận đơn còn hiệu lực. Thông báo này không xác nhận đã thanh toán.</p>",
                IsMandatory = true, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000010"), Code = "AUCTION_WINNER_PUSH", NotificationType = NotificationType.AuctionWon, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Bạn đã thắng đấu giá",
                BodyTemplate = "Bạn đã thắng phiên {{AuctionTitle}} với giá {{WinningAmount}} VND, chốt lúc {{SettledAtUtc}}. Hạn thanh toán: {{PaymentDeadlineUtc}}. Kiểm tra đơn còn hiệu lực trong ứng dụng Nexus trước khi thanh toán.",
                IsMandatory = false, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-00000000000e"), Code = "AUCTION_BID_OUTBID_PUSH",
                NotificationType = NotificationType.AuctionOutbid, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Bạn đã bị vượt giá",
                BodyTemplate = "Bạn đã bị vượt giá tại phiên {{AuctionTitle}}. Mức giá mới ghi nhận lúc {{OccurredAtUtc}}: {{NewAmount}} VND.",
                IsMandatory = false, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-00000000000c"), Code = "ORDER_PAID_BUYER_PUSH",
                NotificationType = NotificationType.OrderPaid, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Thanh toán đơn hàng thành công", BodyTemplate = "Bạn đã thanh toán thành công đơn hàng {{OrderNo}}. Tổng thanh toán: {{TotalAmount}} VND.",
                IsMandatory = true, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-00000000000d"), Code = "ORDER_PAID_SELLER_PUSH",
                NotificationType = NotificationType.OrderPaid, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Bạn có đơn hàng mới", BodyTemplate = "Bạn có đơn hàng mới {{OrderNo}} đã được thanh toán. Vui lòng xác nhận trước {{AcceptDeadlineUtc}}.",
                IsMandatory = true, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-00000000000a"), Code = "ACCOUNT_BANNED_EMAIL",
                NotificationType = NotificationType.SecurityAlert, ChannelCode = ChannelType.Email,
                SubjectTemplate = "Thông báo tạm khóa tài khoản do vi phạm",
                BodyTemplate = "<p>Xin chào {{FullName}},</p><p>Vào lúc {{ChangedAtUtc}}, tài khoản Nexus của bạn đã được tạm khóa do vi phạm quy định.</p><p><strong>Lý do:</strong> {{Reason}}</p><p>Tài khoản bị đình chỉ hoạt động cho đến khi được mở khóa. Nếu cần hỗ trợ, hãy liên hệ bộ phận hỗ trợ Nexus.</p>",
                IsMandatory = true, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-00000000000b"), Code = "ACCOUNT_UNBANNED_EMAIL",
                NotificationType = NotificationType.SecurityAlert, ChannelCode = ChannelType.Email,
                SubjectTemplate = "Thông báo mở khóa tài khoản",
                BodyTemplate = "<p>Xin chào {{FullName}},</p><p>Vào lúc {{ChangedAtUtc}}, tài khoản Nexus của bạn đã được mở khóa về trạng thái hoạt động.</p><p><strong>Lý do:</strong> {{Reason}}</p><p>Thông báo này xác nhận việc khôi phục quyền sử dụng tài khoản tại thời điểm nêu trên.</p>",
                IsMandatory = true, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000008"), Code = "SELLER_APPROVED_EMAIL",
                NotificationType = NotificationType.SecurityAlert, ChannelCode = ChannelType.Email,
                SubjectTemplate = "Yêu cầu trở thành Seller đã được chấp thuận",
                BodyTemplate = "<p>Xin chào {{FullName}},</p><p>Yêu cầu trở thành Seller cho gian hàng {{ShopName}} đã được chấp thuận lúc {{DecidedAtUtc}}.</p><p>Tài khoản hiện tại của bạn đã được cấp thêm quyền Seller. Bạn có thể truy cập Portal Người bán để bắt đầu bán hàng.</p>",
                IsMandatory = true, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000009"), Code = "SELLER_REJECTED_EMAIL",
                NotificationType = NotificationType.SecurityAlert, ChannelCode = ChannelType.Email,
                SubjectTemplate = "Kết quả xét duyệt yêu cầu trở thành Seller",
                BodyTemplate = "<p>Xin chào {{FullName}},</p><p>Yêu cầu trở thành Seller cho gian hàng {{ShopName}} đã bị từ chối lúc {{DecidedAtUtc}}.</p><p><strong>Lý do:</strong> {{RejectionReason}}</p>",
                IsMandatory = true, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000006"),
                Code = "PASSWORD_CHANGED_EMAIL",
                NotificationType = NotificationType.SecurityAlert, ChannelCode = ChannelType.Email,
                SubjectTemplate = "Mật khẩu của bạn đã được thay đổi",
                BodyTemplate = "<p>Xin chào {{FullName}},</p><p>Mật khẩu tài khoản Nexus của bạn đã được thay đổi thành công lúc {{ChangedAtUtc}}.</p><p>Nếu bạn không thực hiện thao tác này, hãy liên hệ bộ phận hỗ trợ Nexus ngay.</p>",
                IsMandatory = true, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000007"),
                Code = "PASSWORD_RESET_SUCCEEDED_EMAIL",
                NotificationType = NotificationType.SecurityAlert, ChannelCode = ChannelType.Email,
                SubjectTemplate = "Mật khẩu của bạn đã được đặt lại",
                BodyTemplate = "<p>Xin chào {{FullName}},</p><p>Mật khẩu tài khoản Nexus của bạn đã được đặt lại thành công lúc {{ChangedAtUtc}}.</p><p>Nếu bạn không thực hiện thao tác này, hãy liên hệ bộ phận hỗ trợ Nexus ngay.</p>",
                IsMandatory = true, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000004"), Code = "ORDER_PAID_PUSH",
                NotificationType = NotificationType.OrderPaid, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Đơn hàng {{OrderNo}} đã thanh toán",
                BodyTemplate = "Đơn hàng {{OrderNo}} đã thanh toán {{TotalAmount}}.", IsMandatory = true,
                Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000005"), Code = "AUCTION_OUTBID_PUSH",
                NotificationType = NotificationType.AuctionOutbid, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Bạn đã bị vượt giá",
                BodyTemplate = "{{AuctionTitle}} đã có mức giá mới {{BidAmount}}.", IsMandatory = false,
                Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000003"),
                Code = "ORDER_PAID_IN_APP", NotificationType = NotificationType.OrderPaid,
                ChannelCode = ChannelType.InApp,
                SubjectTemplate = "Đơn hàng {{OrderNo}} đã thanh toán",
                BodyTemplate = "Bạn có đơn hàng {{OrderNo}} đã thanh toán số tiền {{TotalAmount}}.",
                IsMandatory = true, Locale = "vi-VN", IsActive = true,
                CreatedAt = seedDate, UpdatedAt = seedDate, IsDeleted = false
            },
            new NotificationTemplate
            {
                Id = templateOrderPaidId,
                Code = "ORDER_PAID_EMAIL",
                NotificationType = NotificationType.OrderPaid,
                ChannelCode = ChannelType.Email,
                SubjectTemplate = "Xác nhận thanh toán đơn hàng {{OrderNo}}",
                BodyTemplate = "Đơn hàng {{OrderNo}} của bạn đã được thanh toán thành công số tiền {{TotalAmount}}.",
                IsMandatory = true,
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
        var notificationRecordId = Guid.Parse("61000000-0000-0000-0000-000000000001");
        modelBuilder.Entity<NotificationRecord>().HasData(
            new NotificationRecord
            {
                Id = notificationRecordId,
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

        // 4. Notification Preferences
        modelBuilder.Entity<NotificationPreference>().HasData(
            new NotificationPreference
            {
                Id = Guid.Parse("62000000-0000-0000-0000-000000000001"),
                UserId = buyerUserId,
                NotificationType = NotificationType.OrderPaid,
                ChannelCode = ChannelType.Email,
                IsDisabled = false,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new NotificationPreference
            {
                Id = Guid.Parse("62000000-0000-0000-0000-000000000002"),
                UserId = buyerUserId,
                NotificationType = NotificationType.OrderPaid,
                ChannelCode = ChannelType.InApp,
                IsDisabled = false,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new NotificationPreference
            {
                Id = Guid.Parse("62000000-0000-0000-0000-000000000003"),
                UserId = buyerUserId,
                NotificationType = NotificationType.OrderShipped,
                ChannelCode = ChannelType.Email,
                IsDisabled = false,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );

        // 5. Device Tokens
        modelBuilder.Entity<DeviceToken>().HasData(
            new DeviceToken
            {
                Id = Guid.Parse("63000000-0000-0000-0000-000000000001"),
                UserId = buyerUserId,
                Platform = DevicePlatform.Android,
                Token = "fcm_token_buyer_android_nexus_2026",
                IsActive = true,
                LastSeenAt = seedDate,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            },
            new DeviceToken
            {
                Id = Guid.Parse("63000000-0000-0000-0000-000000000002"),
                UserId = buyerUserId,
                Platform = DevicePlatform.Web,
                Token = "web_push_token_buyer_chrome_nexus_2026",
                IsActive = true,
                LastSeenAt = seedDate,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );

        // 6. Notification Deliveries
        var deliveryEmailId = Guid.Parse("64000000-0000-0000-0000-000000000001");
        modelBuilder.Entity<NotificationDelivery>().HasData(
            new NotificationDelivery
            {
                Id = deliveryEmailId,
                NotificationId = notificationRecordId,
                ChannelCode = ChannelType.Email,
                Destination = "buyer@nexus.com",
                Status = DeliveryStatus.Sent,
                AttemptCount = 1,
                LastAttemptAt = seedDate.AddSeconds(10),
                NextRetryAt = null,
                ErrorCode = null,
                ErrorMessage = null,
                RowVersion = 1,
                CreatedAt = seedDate,
                UpdatedAt = seedDate.AddSeconds(10),
                IsDeleted = false
            },
            new NotificationDelivery
            {
                Id = Guid.Parse("64000000-0000-0000-0000-000000000002"),
                NotificationId = notificationRecordId,
                ChannelCode = ChannelType.InApp,
                Destination = buyerUserId.ToString(),
                Status = DeliveryStatus.Sent,
                AttemptCount = 1,
                LastAttemptAt = seedDate,
                NextRetryAt = null,
                ErrorCode = null,
                ErrorMessage = null,
                RowVersion = 1,
                CreatedAt = seedDate,
                UpdatedAt = seedDate,
                IsDeleted = false
            }
        );

        // 7. Notification Logs
        modelBuilder.Entity<NotificationLog>().HasData(
            new NotificationLog
            {
                Id = Guid.Parse("65000000-0000-0000-0000-000000000001"),
                NotificationId = notificationRecordId,
                DeliveryId = deliveryEmailId,
                Action = LogAction.Sent,
                Level = LogLevel.Info,
                Message = "Email thông báo thanh toán đơn hàng đã được gửi thành công qua SMTP",
                Metadata = "{\"provider\":\"Smtp\",\"recipient\":\"buyer@nexus.com\"}",
                CreatedAt = seedDate.AddSeconds(10),
                UpdatedAt = seedDate.AddSeconds(10),
                IsDeleted = false
            }
        );

        // 8. Audit Log
        modelBuilder.Entity<AuditLog>().HasData(
            new AuditLog
            {
                Id = Guid.Parse("66000000-0000-0000-0000-000000000001"),
                ServiceName = "Notification",
                TraceId = "trace-notif-pref-001",
                UserId = buyerUserId.ToString(),
                UserName = "buyer@nexus.com",
                Action = AuditAction.PreferenceUpdate,
                EntityName = "NotificationPreference",
                EntityId = "62000000-0000-0000-0000-000000000001",
                OldValues = null,
                NewValues = "{\"ChannelCode\":\"Email\",\"NotificationType\":\"OrderPaid\",\"IsDisabled\":false}",
                ChangedColumns = "[\"IsDisabled\"]",
                IpAddress = "127.0.0.1",
                UserAgent = "NexusClient/1.0",
                CreatedAt = seedDate,
                IsSynced = true
            }
        );
    }
}
