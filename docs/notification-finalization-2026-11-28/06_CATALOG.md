# Yêu cầu cho CatalogService

## Hợp đồng đã đối chiếu

Notification client trong `origin/dev` gọi `POST api/v1/internal/notifications/in-app`; Notification `origin/dev` **có** `InternalNotificationsController` nhận route này, với cùng các trường `userId`, `sourceEventId`, `title`, `body`, `notificationType`, `payload`. Không cần đổi route. Cần kiểm tra danh tính service ở runtime, cấu hình base URL và quyền truy cập; generic `email/send` cũng cần đối chiếu quyền service và trạng thái gửi thực tế.

## Nghiệp vụ

Các caller Product, ProductReport, ProductReview phải gửi sau commit, đúng người nhận, truyền `sourceEventId` ổn định (client hiện cho phép trường này nhưng là optional) và liên kết trace qua header, không làm thao tác sản phẩm phụ thuộc Notification. Cần tách: báo cáo sản phẩm được xử lý/đánh giá mới là in-app thường; gỡ sản phẩm/vi phạm ảnh hưởng quyền bán là sự kiện quan trọng, xem xét email + in-app với lý do rõ ràng. Không gửi cùng nội dung qua cả generic email và event typed.

**PASS:** test từng caller với service auth thật; không 404/401; retry cùng event không tạo hai bản ghi; đúng người nhận và link sản phẩm/báo cáo.
