# Yêu cầu cho FulfillmentService

## Lệch hợp đồng hiện thấy

`origin/dev` có client `SendShipmentEventAsync` trỏ `/api/v1/notifications/shipment-events`, nhưng chưa thấy production caller dùng nó. GHN webhook/simulation đang gọi `SendDeliveryAlertAsync` qua `/api/v1/notifications/in-app`; đường generic này chưa thấy `sourceEventId` nên retry có thể tạo trùng. Nhãn `OrderCompleted` được dùng ngay lúc GHN báo `Delivered`, trước khi Buyer xác nhận nhận hàng.

## Cần phía Fulfillment

1. Sau khi trạng thái GHN được kiểm tra, lưu và commit, phát một event vận chuyển typed với `shipmentId/orderId`, `sourceVersion` hoặc mã webhook, trạng thái chuẩn hóa, Buyer/Seller IDs, thời điểm, `sourceEventId`, `traceId`.
2. Chọn **một** đường phát chính cho cùng sự kiện: chuyển sang `shipment-events` nếu contract Notification hỗ trợ đầy đủ; ngừng phát song song generic in-app để tránh hai chuông. Ghi outbox/retry sau commit, xử lý webhook lặp/out-of-order.
3. Phân biệt `Delivered` (GHN giao tới nơi) với `OrderCompleted` (Commerce hoàn tất theo chính sách). Chỉ Commerce hoặc owner được thống nhất phát sự kiện OrderCompleted.
4. Đối chiếu trạng thái cần hành động: giao thất bại, hoàn hàng, cần Seller xử lý; xác định email + in-app. Đang vận chuyển/đang giao: in-app.

**PASS:** một webhook GHN lặp nhiều lần vẫn một thông báo logic; thứ tự trạng thái không lùi; Buyer/Seller thấy đúng thông tin và link đơn.
