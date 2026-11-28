# Kế hoạch DB server, chạy dịch vụ và email giao dịch

Tài liệu là kế hoạch, **chưa** thay cấu hình, migrate DB hoặc deploy trong lượt này.

## DB chung theo hạ tầng, riêng dữ liệu theo service

Các service có thể cùng dùng một máy/cluster Neon, nhưng User, Auction, Commerce, Fulfillment, Catalog, Wallet, Notification và Log dùng DB/schema/credential đúng quyền sở hữu. Notification lưu thông báo, receipt và preferences trong DB của mình; chỉ nhận dữ kiện của service khác qua REST. Không copy bảng, join chéo DB hoặc dùng một credential toàn quyền cho mọi service.

Trước khi chuyển server: kiểm kê tên DB/role, migration history, row count/index; backup từng DB, kiểm tra restore trên fixture; chạy migration trên bản sao, review SQL chỉ có thay đổi cộng thêm; tắt auto-migrate/seed ở runtime; sau đó mới áp lần lượt lên DB thật. Không ghi bí mật vào Git. Mỗi service có health/readiness cho DB, kiểm tra timeout và pool size. Với Notification, kiểm tra key ring OTP dùng chung giữa replica, chỉ một nhóm worker hợp lệ, lease chống xử lý trùng và queue tồn đọng trước khi bật live delivery.

## Email ổn định hơn Gmail cá nhân

Đề xuất cho môi trường Render Free: nhà cung cấp email giao dịch qua **HTTPS API cổng 443** như Resend hoặc Amazon SES API, với domain đã xác minh SPF/DKIM/DMARC. Render Free chặn SMTP outbound 25/465/587, nên chỉ đổi tài khoản Gmail không xử lý gốc vấn đề trên nền tảng này ([Render](https://render.com/articles/can-i-send-email-over-smtp-from-render)).

Không có dịch vụ email “không giới hạn” theo nghĩa bảo đảm gửi mọi lượng và không bao giờ bị chặn. Resend Free có hạn mức theo ngày; gói trả phí bỏ giới hạn ngày nhưng vẫn theo quota/gói và chính sách chống lạm dụng ([Resend pricing](https://resend.com/pricing)). Amazon SES cũng có sandbox và quota gửi, có thể xin tăng sau khi được duyệt ([AWS SES quotas](https://docs.aws.amazon.com/ses/latest/dg/quotas.html)). Chọn provider theo sản lượng thực tế, ngân sách và domain sở hữu; tránh cam kết vô hạn.

Thay sender trong Notification bằng interface chung, cấu hình provider qua secret của server, hỗ trợ idempotency key/provider message ID, retry 429/5xx có backoff và tách lỗi 4xx vĩnh viễn. Thu thập bounce/complaint qua webhook có xác minh chữ ký; suppression list, tránh gửi liên tiếp OTP không cần thiết. Template có bản HTML responsive và text, tiếng Việt rõ, link đúng domain production, không lộ bí mật. Triển khai thử đến hộp thư đội dự án rồi đối chiếu delivery provider với thư nhận thực tế.

## Realtime trên server

Auction room SignalR phục vụ giá phiên; hub Notification theo user phục vụ chuông/cảnh báo. Reverse proxy phải hỗ trợ WebSocket WSS, authentication, reconnect và scale-out nếu nhiều replica. Không phát realtime từ request trước DB commit. Nếu server ngủ hoặc mạng đứt, FE phải bắt kịp qua REST; riêng push nền cần provider/browser permission riêng. Render Free có thể ngủ và không phù hợp làm worker gửi email/OTP liên tục; cần kiểm tra mức dịch vụ hoặc host worker bền vững trước khi cam kết độ trễ ([Render Free](https://render.com/docs/free)).

## Các cổng nghiệm thu theo thứ tự

1. Hợp đồng caller/callee và test fixture: Wallet top-up, Catalog, Commerce quyết định đơn, Fulfillment shipment.
2. Backup + restore fixture + migration dry-run, rồi duyệt chuyển DB server riêng từng service.
3. Kết nối HTTPS API email, domain verified, OTP/Seller/ban/tiền quan trọng tới hộp thư thật.
4. Realtime Auction + Notification qua Gateway và FE, đo độ trễ, thử offline/reconnect/nhiều tab.
5. Full flow FE: đăng ký, Seller, mua hàng, nạp ví, GHN, đấu giá; đối chiếu DB nguồn, DB Notification, receipt, inbox và giao diện.

Không đánh dấu PASS nếu chỉ có HTTP 200/202, trạng thái `Pending` hoặc một toast FE không có bản ghi bền vững.
