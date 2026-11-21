# Contract thông báo kiểm duyệt sản phẩm — D-10b

Ngày: 20/11/2026. Phạm vi Notification: nhận diện type, lưu in-app đúng người, chống replay theo báo cáo và không phát biểu số điểm phạt chưa được xác nhận. Việc cấm sản phẩm đang đấu giá (D-13), áp dụng điểm và xác định người nhận thuộc service nguồn.

## Endpoint công khai

`POST /api/v1/notifications/in-app`: yêu cầu danh tính service/admin/staff theo cơ chế Gateway hiện có. Trình duyệt Buyer/Seller không được phát thông báo cho userId tùy ý. Caller không tự giả header người dùng khi đi Gateway.

```json
{
  "userId": "10000000-0000-4000-8000-000000000001",
  "title": "Sản phẩm bị cấm",
  "body": "Kết quả kiểm duyệt sản phẩm",
  "notificationType": "ProductBannedByReport",
  "payload": "{\"productId\":\"20000000-0000-4000-8000-000000000001\",\"reportId\":\"30000000-0000-4000-8000-000000000001\"}",
  "sourceEventId": "40000000-0000-4000-8000-000000000001"
}
```

Payload là **chuỗi JSON** theo DTO in-app hiện có, không phải object trực tiếp. User/product/report là UUID khác rỗng; title/body không rỗng; body request tối đa 16 KiB. SourceEventId UUID tùy chọn; nên gửi ID ổn định từ quyết định đã commit.

| Alias caller cũ được nhận tại ingress | Type lưu/trả API | DB | Người nhận do nguồn chọn |
|---|---|---|---|
| PRODUCT_BANNED_REPORT | ProductBannedByReport | 26 | Seller có sản phẩm bị cấm |
| REPORT_RESOLVED | ProductReportResolved | 27 | Người gửi báo cáo được xác nhận |
| REPORT_DISMISSED | ProductReportDismissed | 28 | Người gửi báo cáo bị bác bỏ |

Canonical PascalCase cũng được chấp nhận. API danh sách/filter dùng canonical type. Không mở parser enum toàn cục để nhận mọi chuỗi không biết hoặc số nguyên.

## Nội dung và quyền riêng tư

Notification render nội dung trung tính cho ba type này từ kết quả đã khai báo; không lặp lại title/body tùy ý, câu “trừ 10 điểm”, penaltyPoints hoặc ghi chú quản trị từ caller cũ. Payload cá nhân chỉ lưu productId/reportId và metadata của Notification, không có danh sách người nhận hoặc ghi chú nội bộ.

Không đổi “10” thành “5” bằng phỏng đoán. Service nguồn phải đối chiếu kết quả phạt thực tế trước khi bổ sung thông báo điểm riêng. Hiện thông báo này chỉ khẳng định quyết định kiểm duyệt/báo cáo đã được nguồn xác nhận.

## Replay và phản hồi

Nếu chưa gửi sourceEventId, receiver suy ra khóa ổn định từ reportId + canonical type + userId trong namespace catalog. Nếu có sourceEventId thì dùng ID nguồn. Payload hash chỉ chứa dữ liệu nghiệp vụ được nhận, không chứa nội dung đã loại bỏ. Cùng ID với product/report/recipient/type khác trả 409 SERVICE_EVENT_CONFLICT. Cùng snapshot không tạo thêm thông báo, kể cả sau ẩn/xóa mềm.

HTTP 200 trả InAppNotificationItemResponse trực tiếp (id, userId, title, body, payload, notificationType, isRead, ...). HTTP 204 khi receipt được lưu nhưng thông báo bị tắt/ẩn. Không coi 204 là đã hiển thị trên inbox. 400 khi thiếu/sai ID, 401 khi chưa có danh tính, 403 khi thiếu role, 409 khi xung đột. Không cần migration Notification.

## Caller cũ còn cần sửa

Đối chiếu đầy đủ DI sau pull xác nhận Catalog đã có IdentityPropagationHandler (truyền danh tính Admin/Staff từ Gateway) và InternalServiceAuthHandler (key cấu hình). Bản client cũ vẫn gọi `/api/v1/internal/notifications/in-app`. Bản sửa local đã chuyển sang public in-app, giữ handler chain, chuẩn hóa type và gửi sourceEventId. Không cần thêm cơ chế xác thực Catalog mới. Endpoint legacy đang tồn tại nhưng chịu InternalServiceAuthMiddleware; Gateway có thể chặn prefix. Bản sửa **không bỏ kiểm tra key, không thêm alias route và không mở anonymous**.

Bản sửa Catalog giữ danh tính tác nhân đã được xác thực và gọi trực tiếp backend trong mạng tin cậy; không đưa header định danh qua Gateway như dữ liệu trình duyệt. Retry của quyết định report dùng event ID ổn định; các chu kỳ kiểm duyệt sản phẩm khác nhau có ID riêng. Tiếp tục đối chiếu điểm từ kết quả phạt thực tế. Không gửi cùng quyết định vào cả endpoint cũ và mới.

## Checklist gửi nhóm Catalog

- [x] Chuyển URL trong bản sửa local; kiểm chứng Admin/Staff qua handler chain và HTTP receiver.
- [ ] BAN_PRODUCT gửi Seller; RESOLVED/DISMISSED gửi đúng reporter.
- [ ] Payload có productId/reportId UUID hợp lệ; retry giữ event ID.
- [ ] Bỏ câu điểm cố định trong caller; đối chiếu kết quả User nếu cần thông báo điểm.
- [ ] Không cấm sản phẩm Scheduled/Running nếu chưa xử lý Auction theo D-13.
- [ ] Kiểm chứng thao tác thật từ Catalog đến inbox; không dùng POST Notification thủ công để kết luận toàn luồng đạt.

## Bổ sung bốn trạng thái kiểm duyệt sản phẩm

| Alias | Canonical | DB |
|---|---|---|
| PRODUCT_APPROVED | ProductApproved | 29 |
| PRODUCT_REJECTED | ProductRejected | 30 |
| PRODUCT_SUSPENDED | ProductSuspended | 31 |
| PRODUCT_BANNED | ProductBanned | 32 |

Bốn type này yêu cầu productId UUID và sourceEventId UUID khác rỗng; không cần reportId. Receiver render nội dung trung tính, không đưa điểm phạt hoặc ghi chú quản trị vào thông báo. Role Staff được nhận ở public in-app vì nghiệp vụ kiểm duyệt đã cho phép Staff.

Client tạo ID mới cho mỗi lần phát quyết định kiểm duyệt thông thường để không gộp nhầm lần duyệt lại sau khi bị tạm ẩn. ID được giữ trong body của lần gửi đó. Chưa có cơ chế gửi lại bền vững từ audit cho các quyết định thông thường; không coi một lệnh gọi client mới sau lỗi là replay an toàn của cùng quyết định. Report dùng ID suy ra từ report/type/recipient nên phát lại cùng quyết định không trùng.
