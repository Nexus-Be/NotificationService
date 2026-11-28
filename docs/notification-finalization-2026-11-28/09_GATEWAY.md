# Yêu cầu cho Gateway

`origin/dev` đã có route SignalR `/hubs/auctions/{**catch-all}` và route Notification; Auction service token được kiểm tra ở các route đấu giá nhạy cảm. Cần kiểm tra cấu hình **runtime server** chứ không dựa riêng `appsettings.example.json`.

1. Route `/api/v1/notifications/me`, unread count, read/unread dùng JWT user, không cho đọc người khác. Route POST nội bộ phải có danh tính service, không mở cho browser; đặc biệt `wallet-topups` sau khi Notification merge.
2. WebSocket upgrade/SignalR negotiate tới Auction hoạt động qua domain HTTPS/WSS, CORS đúng origin FE, timeout của reverse proxy đủ; test reconnect trên server. Nếu có hub cá nhân Notification, thêm route và chính sách auth tương tự, phân biệt quyền room user với room đấu giá công khai.
3. Đảm bảo chế độ service là Real, URL cluster đúng, không mock 200; trace ID từ FE qua Gateway tới nguồn và Notification; 401/403/404/502 được phân biệt rõ.
4. Giải merge local `docker-compose.yml` với `origin/dev` trước build chung; giữ cấu hình private ngoài Git.

**PASS:** kết nối FE qua Gateway đọc chuông, đặt bid và nhận SignalR, ngắt/kết nối lại không mất thông báo; request trình duyệt giả mạo endpoint phát email bị chặn.
