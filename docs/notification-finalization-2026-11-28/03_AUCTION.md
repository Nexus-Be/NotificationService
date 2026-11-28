# Yêu cầu cho AuctionService — cảnh báo vượt giá kịp thời

## Hiện trạng `origin/dev`

Auction đã có SignalR `/hubs/auctions`, event `BidPlaced` theo nhóm phiên, và outbox gửi `auction-outbids` sang Notification sau khi bid được ghi. Đây là nền tốt cho giá trực tiếp và thông báo bền vững. Nhóm SignalR theo phiên chỉ đến người đang xem phiên đó; Buyer ở trang khác, tab ngủ hoặc mất mạng cần đường cá nhân và catch-up riêng.

## Cần từ Auction

1. Giữ nguồn sự thật là bid đã commit. Với mỗi lần người dẫn giá cũ bị vượt, outbox lưu `auctionId`, `triggerBidId`, `outbidBidId`, `recipientUserId`, `newPrice`, `endAt`, `sourceEventId` ổn định, `traceId`; không tạo sự kiện từ click/giá dự kiến.
2. Đảm bảo dispatcher có retry/backoff và cảnh báo khi outbox quá tuổi. Đo độ trễ từ `bid committed` đến Notification persisted, đến FE received; công bố ngưỡng mục tiêu cho demo/server. Không dựa vào SignalR room để thay thông báo bền vững.
3. Các sự kiện đấu giá quan trọng: thắng, thua/kết thúc, seller result, cần thanh toán, hết hạn thanh toán, hủy, hoàn cọc. Đối chiếu route/DTO `origin/dev` Notification và ghi rõ người nhận của từng trạng thái. Không gửi mail cho mọi lần vượt giá mặc định; ưu tiên in-app realtime, tránh spam.
4. Kiểm tra cạnh tranh: hai bid gần nhau, nhiều tab cùng Buyer, bid cuối giây, replay outbox, server Notification tắt/rồi phục hồi, mất kết nối WebSocket. Tình trạng Buyer không nhận thông báo không được làm bid rollback, nhưng phải có retry và đối soát.
5. Nếu có gia hạn giờ chốt khi bid sát hạn, thể hiện thời gian cuối cùng từ nguồn Auction; FE không tự suy đoán. Đây là quyết định nghiệp vụ cần chốt với nhóm Auction.

## Hợp đồng realtime đề nghị

Auction tiếp tục phát `BidPlaced` vào room phiên để cập nhật giá. Notification phát `NotificationCreated` vào room **user đã xác thực** sau khi lưu in-app. FE dùng event để làm mới chuông và hiển thị cảnh báo; sau reconnect gọi `GET /api/v1/notifications/me` và trạng thái phiên. `notificationId/sourceEventId` dùng khử trùng lặp trên FE. Không đưa danh sách người tham gia, email hoặc token vào event room chung.

**PASS:** Buyer bị vượt giá khi đang ở trang khác nhận cảnh báo trực tiếp; khi offline thấy cùng bản ghi sau mở lại; mọi bid vẫn đúng DB và chỉ có một thông báo logic cho một sự kiện.
