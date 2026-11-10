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
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000037"),Code = "ORDER_BUYER_CANCELLED_BUYER_PUSH",NotificationType = NotificationType.OrderBuyerCancelled,ChannelCode = ChannelType.Push,SubjectTemplate = "Đơn hàng đã được hủy theo yêu cầu người mua",BodyTemplate = "Đơn {{OrderNo}} đã được hủy theo yêu cầu của bạn lúc {{CancelledAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra đơn và tiến trình hoàn tiền hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000038"),Code = "ORDER_BUYER_CANCELLED_SELLER_PUSH",NotificationType = NotificationType.OrderBuyerCancelled,ChannelCode = ChannelType.Push,SubjectTemplate = "Đơn hàng đã được hủy theo yêu cầu người mua",BodyTemplate = "Người mua đã hủy đơn {{OrderNo}} lúc {{CancelledAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn hiện tại trong Nexus trước khi tiếp tục chuẩn bị hàng.",Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000036"),Code = "ORDER_PAYMENT_EXPIRED_PUSH",NotificationType = NotificationType.OrderPaymentExpired,ChannelCode = ChannelType.Push,SubjectTemplate = "Đơn hàng đã hủy do quá hạn thanh toán",BodyTemplate = "Đơn {{OrderNo}} đã bị hủy do quá hạn xác nhận thanh toán,ghi nhận lúc {{CancelledAtUtc}}. Hạn gốc: {{PaymentDeadlineUtc}}. Vui lòng kiểm tra đơn và trạng thái thanh toán hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000035"),Code = "ORDER_SELLER_SETTLED_PUSH",NotificationType = NotificationType.OrderSellerSettled,ChannelCode = ChannelType.Push,SubjectTemplate = "Đã ghi nhận cộng tiền vào ví người bán",BodyTemplate = "Đơn {{OrderNo}} đã ghi nhận cộng {{NetAmount}} VND vào ví người bán,lúc {{SettledAtUtc}}. Vui lòng kiểm tra giao dịch và số dư hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000033"),Code = "ORDER_COMPLETED_BUYER_PUSH",NotificationType = NotificationType.OrderCompleted,ChannelCode = ChannelType.Push,SubjectTemplate = "Đơn hàng đã hoàn tất",BodyTemplate = "Đơn {{OrderNo}} đã hoàn tất,ghi nhận lúc {{CompletedAtUtc}}. Vui lòng mở đơn trong Nexus để xem chi tiết và các lựa chọn đánh giá hiện có.",Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000034"),Code = "ORDER_COMPLETED_SELLER_PUSH",NotificationType = NotificationType.OrderCompleted,ChannelCode = ChannelType.Push,SubjectTemplate = "Đơn hàng đã hoàn tất",BodyTemplate = "Đơn {{OrderNo}} đã hoàn tất,ghi nhận lúc {{CompletedAtUtc}}. Vui lòng kiểm tra chi tiết đơn và trạng thái thanh toán cho người bán trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000021"),Code = "SHIPMENT_CREATED_BUYER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: Vận đơn GHN đã được tạo,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000022"),Code = "SHIPMENT_CREATED_SELLER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: Vận đơn GHN đã được tạo,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000023"),Code = "SHIPMENT_PICKED_UP_BUYER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận đã lấy hàng,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000024"),Code = "SHIPMENT_PICKED_UP_SELLER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận đã lấy hàng,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000025"),Code = "SHIPMENT_IN_TRANSIT_BUYER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận hàng đang được vận chuyển,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000026"),Code = "SHIPMENT_IN_TRANSIT_SELLER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận hàng đang được vận chuyển,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000027"),Code = "SHIPMENT_OUT_FOR_DELIVERY_BUYER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận đang giao hàng,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000028"),Code = "SHIPMENT_OUT_FOR_DELIVERY_SELLER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận đang giao hàng,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000029"),Code = "SHIPMENT_DELIVERED_BUYER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận đã giao hàng thành công,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-00000000002a"),Code = "SHIPMENT_DELIVERED_SELLER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận đã giao hàng thành công,ghi nhận lúc {{OccurredAtUtc}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-00000000002b"),Code = "SHIPMENT_DELIVERY_FAILED_BUYER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận giao hàng không thành công,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-00000000002c"),Code = "SHIPMENT_DELIVERY_FAILED_SELLER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận giao hàng không thành công,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-00000000002d"),Code = "SHIPMENT_RETURNING_BUYER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận hàng đang chuyển hoàn,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-00000000002e"),Code = "SHIPMENT_RETURNING_SELLER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận hàng đang chuyển hoàn,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-00000000002f"),Code = "SHIPMENT_RETURNED_BUYER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận hàng đã chuyển hoàn,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000030"),Code = "SHIPMENT_RETURNED_SELLER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận hàng đã chuyển hoàn,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000031"),Code = "SHIPMENT_CANCELLED_BUYER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận vận đơn đã bị hủy,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-000000000032"),Code = "SHIPMENT_CANCELLED_SELLER_PUSH",NotificationType = NotificationType.OrderShipped,ChannelCode = ChannelType.Push,SubjectTemplate = "Cập nhật vận chuyển đơn hàng",BodyTemplate = "Đơn {{OrderNo}},mã vận đơn {{TrackingCode}}: GHN ghi nhận vận đơn đã bị hủy,ghi nhận lúc {{OccurredAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và vận đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = false,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000020"),Code = "ORDER_REFUND_COMPLETED_PUSH",NotificationType = NotificationType.OrderRefunded,ChannelCode = ChannelType.Push,
                SubjectTemplate = "Hoàn tiền vào ví Buyer thành công",
                BodyTemplate = "Đơn {{OrderNo}} đã được hoàn {{RefundAmount}} VND vào ví Buyer,ghi nhận lúc {{CompletedAtUtc}}. Vui lòng kiểm tra giao dịch và số dư hiện tại trong Nexus.",
                Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate
            },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-00000000001a"),Code = "ORDER_ACCEPTED_BUYER_PUSH",NotificationType = NotificationType.OrderDecision,ChannelCode = ChannelType.Push,SubjectTemplate = "Seller đã chấp nhận đơn",BodyTemplate = "Seller đã chấp nhận xử lý đơn {{OrderNo}},ghi nhận lúc {{DecidedAtUtc}}. Vui lòng kiểm tra trạng thái đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-00000000001b"),Code = "ORDER_ACCEPTED_SELLER_PUSH",NotificationType = NotificationType.OrderDecision,ChannelCode = ChannelType.Push,SubjectTemplate = "Seller đã chấp nhận đơn",BodyTemplate = "Seller đã chấp nhận xử lý đơn {{OrderNo}},ghi nhận lúc {{DecidedAtUtc}}. Vui lòng kiểm tra trạng thái đơn hiện tại trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-00000000001c"),Code = "ORDER_REJECTED_BUYER_PUSH",NotificationType = NotificationType.OrderDecision,ChannelCode = ChannelType.Push,SubjectTemplate = "Đơn hàng đã bị hủy do Seller từ chối",BodyTemplate = "Đơn {{OrderNo}} bị hủy do Seller từ chối,ghi nhận lúc {{DecidedAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và hoàn tiền trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-00000000001d"),Code = "ORDER_REJECTED_SELLER_PUSH",NotificationType = NotificationType.OrderDecision,ChannelCode = ChannelType.Push,SubjectTemplate = "Đơn hàng đã bị hủy do Seller từ chối",BodyTemplate = "Đơn {{OrderNo}} bị hủy do Seller từ chối,ghi nhận lúc {{DecidedAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và hoàn tiền trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-00000000001e"),Code = "ORDER_ACCEPTANCE_TIMEOUT_BUYER_PUSH",NotificationType = NotificationType.OrderDecision,ChannelCode = ChannelType.Push,SubjectTemplate = "Đơn hàng đã bị hủy do quá hạn chấp nhận",BodyTemplate = "Đơn {{OrderNo}} bị hủy do quá hạn Seller chấp nhận,ghi nhận lúc {{DecidedAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và hoàn tiền trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate { Id = Guid.Parse("60000000-0000-0000-0000-00000000001f"),Code = "ORDER_ACCEPTANCE_TIMEOUT_SELLER_PUSH",NotificationType = NotificationType.OrderDecision,ChannelCode = ChannelType.Push,SubjectTemplate = "Đơn hàng đã bị hủy do quá hạn chấp nhận",BodyTemplate = "Đơn {{OrderNo}} bị hủy do quá hạn Seller chấp nhận,ghi nhận lúc {{DecidedAtUtc}}. Lý do: {{Reason}}. Vui lòng kiểm tra trạng thái đơn và hoàn tiền trong Nexus.",Locale = "vi-VN",IsActive = true,IsMandatory = true,CreatedAt = seedDate,UpdatedAt = seedDate },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000018"), Code = "AUCTION_ORDER_PAID_BUYER_PUSH", NotificationType = NotificationType.OrderPaid, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Đơn đấu giá đã thanh toán thành công", BodyTemplate = "Đơn đấu giá {{OrderNo}} đã thanh toán {{TotalAmount}} VND,ghi nhận lúc {{PaidAtUtc}}. Vui lòng kiểm tra trạng thái đơn hiện tại trong Nexus.", Locale = "vi-VN", IsActive = true, IsMandatory = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000019"), Code = "AUCTION_ORDER_PAID_SELLER_PUSH", NotificationType = NotificationType.OrderPaid, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Đơn đấu giá đã được thanh toán", BodyTemplate = "Đơn đấu giá {{OrderNo}} đã được thanh toán,ghi nhận lúc {{PaidAtUtc}}. Hạn chấp nhận từ nguồn: {{AcceptDeadlineUtc}}. Vui lòng mở đơn trong Nexus,kiểm tra trạng thái và quyền chấp nhận hiện tại trước khi thao tác.", Locale = "vi-VN", IsActive = true, IsMandatory = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000017"), Code = "AUCTION_STARTED_PUSH", NotificationType = NotificationType.AuctionStarted, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Phiên đấu giá đã được kích hoạt",
                BodyTemplate = "Phiên {{AuctionTitle}} được kích hoạt lúc {{StartedAtUtc}}; thời điểm kết thúc dự kiến tại lần kích hoạt: {{AuctionEndsAtUtc}}. Vui lòng kiểm tra trạng thái phiên hiện tại trong Nexus.",
                Locale = "vi-VN", IsActive = true, IsMandatory = false, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000016"), Code = "AUCTION_SELLER_PAYMENT_TIMEOUT_PUSH", NotificationType = NotificationType.AuctionPaymentTimedOut, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Người thắng đã quá hạn thanh toán",
                BodyTemplate = "Người thắng phiên {{AuctionTitle}} đã quá hạn thanh toán (hạn gốc {{PaymentDeadlineUtc}}). Kết quả ghi nhận lúc {{TimedOutAtUtc}}: đơn đã bị hủy, phiên thất bại. Vui lòng kiểm tra phiên và đơn hiện tại trong Nexus.",
                Locale = "vi-VN", IsActive = true, IsMandatory = false, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000014"), Code = "AUCTION_SELLER_WINNER_SELECTED_PUSH", NotificationType = NotificationType.AuctionSellerResult, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Phiên đấu giá đã có người thắng", BodyTemplate = "Kết quả phiên {{AuctionTitle}} ghi nhận lúc {{DecidedAtUtc}}: đã có người thắng với giá {{WinningAmount}} VND; hạn thanh toán gốc {{PaymentDeadlineUtc}}. Vui lòng kiểm tra phiên và đơn hiện tại trong Nexus.",
                Locale = "vi-VN", IsActive = true, IsMandatory = false, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000015"), Code = "AUCTION_SELLER_NO_BIDS_PUSH", NotificationType = NotificationType.AuctionSellerResult, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Phiên đấu giá kết thúc không có bid", BodyTemplate = "Phiên {{AuctionTitle}} thất bại vì không có lượt đặt giá, theo kết quả ghi nhận lúc {{DecidedAtUtc}}. Vui lòng kiểm tra chi tiết phiên hiện tại trong Nexus.",
                Locale = "vi-VN", IsActive = true, IsMandatory = false, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000012"), Code = "AUCTION_PAYMENT_TIMEOUT_EMAIL", NotificationType = NotificationType.AuctionPaymentTimedOut, ChannelCode = ChannelType.Email,
                SubjectTemplate = "Đơn đấu giá đã quá hạn thanh toán",
                BodyTemplate = "<p>Xin chào {{FullName}},</p><p>Bạn đã quá hạn thanh toán đơn {{OrderReference}} của phiên {{AuctionTitle}}. Hạn thanh toán: {{PaymentDeadlineUtc}}.</p><p>Kết quả ghi nhận lúc {{TimedOutAtUtc}}: đơn đã bị hủy, phiên kết thúc thất bại.</p><p>Hãy kiểm tra trạng thái đơn và thông tin uy tín hiện tại trong ứng dụng Nexus. Thông báo này không yêu cầu thanh toán khoản phạt tiền.</p>",
                IsMandatory = true, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000013"), Code = "AUCTION_PAYMENT_TIMEOUT_PUSH", NotificationType = NotificationType.AuctionPaymentTimedOut, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Đơn đấu giá đã quá hạn thanh toán",
                BodyTemplate = "Bạn đã quá hạn thanh toán phiên {{AuctionTitle}} (hạn {{PaymentDeadlineUtc}}). Kết quả hủy đơn ghi nhận lúc {{TimedOutAtUtc}}. Vui lòng kiểm tra trạng thái hiện tại trong ứng dụng Nexus.",
                IsMandatory = false, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("60000000-0000-0000-0000-000000000011"), Code = "AUCTION_CANCELLED_PUSH",
                NotificationType = NotificationType.AuctionCancelled, ChannelCode = ChannelType.Push,
                SubjectTemplate = "Phiên đấu giá đã bị hủy",
                BodyTemplate = "Phiên {{AuctionTitle}} đã bị hủy lúc {{CancelledAtUtc}}. Lý do: {{Reason}}.",
                IsMandatory = true, Locale = "vi-VN", IsActive = true, CreatedAt = seedDate, UpdatedAt = seedDate
            },
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
