# Yêu cầu cho LogService và quan sát liên service

Không cần tạo caller Notification từ LogService nếu không có nghiệp vụ người dùng tương ứng. Vai trò cần thiết là quan sát chuỗi event.

- Thu nhận `traceId`, `sourceEventId`, service phát, loại sự kiện, `notificationId`, channel, trạng thái delivery, provider message ID đã giảm nhạy cảm, thời điểm từng bước. Không ghi OTP, JWT, mật khẩu, địa chỉ đầy đủ nếu không cần.
- Dashboard cho outbox chưa gửi, OTP quá hạn, SMTP/HTTPS provider bị throttle, notification in-app chưa publish realtime, số duplicate/conflict, retry quá mức, worker không healthy.
- Cảnh báo đặc biệt khi outbid quá độ trễ mục tiêu hoặc thời hạn phiên đã tới mà queue vẫn còn; hiển thị tuổi queue theo p95/p99, không chỉ tổng số request.
- Có đường đối soát một event nguồn qua Gateway/Auction/Commerce/Wallet/Fulfillment sang Notification và delivery. Phân biệt lỗi nguồn chưa gọi, Gateway chặn, Notification trả lỗi, provider lỗi và người dùng không thấy FE.

**PASS:** nhập một trace ID từ thao tác FE thấy toàn chuỗi và vị trí lỗi; log không chứa bí mật.
