# Notification — chính sách kênh gửi và tiến độ kiểm chứng 25/11/2026

## Kết luận đúng phạm vi

Đã sửa xác thực caller User/Commerce và tiếp nhận khóa backend mạnh cho email/in-app; bổ sung lập lịch hai kênh trên cùng bản ghi thông báo. Không đổi schema, không migration/reset Neon, không commit/push/deploy Production. Không kết luận toàn bộ flow website đã hoàn tất: một số caller giao hàng/tài chính chưa phát snapshot đúng hợp đồng.

## Chính sách mặc định

| Nhóm | Kênh |
|---|---|
| OTP registration / password_reset / seller_registration | Email; không ghi mã OTP vào quả chuông |
| Đổi/reset mật khẩu thành công, duyệt/từ chối Seller, ban/unban có lý do | Email + in-app |
| Thanh toán đơn, quyết định đơn, hoàn tiền, hoàn tất, giải ngân, hủy/hết hạn đơn | Email + in-app khi có sự kiện nghiệp vụ hợp lệ |
| Thắng/hủy/thất bại phiên, kết quả Seller, quá hạn thanh toán, đề nghị cơ hội tiếp theo, xử lý reserve, biến động cọc | Email + in-app |
| Duyệt/từ chối/hoàn tất/thất bại rút tiền | Email + in-app; chưa bổ sung sự kiện Created hay cảnh báo tới Staff |
| Vi phạm uy tín, khóa/đình chỉ/từ chối sản phẩm, khóa do report | Email + in-app |
| Giao thành công/thất bại, đang trả/đã trả/hủy vận đơn | Email + in-app khi caller shipment-events đã nối |
| Bắt đầu phiên, vượt giá, watchlist bid, kết quả report, sản phẩm được duyệt, khuyến mãi | In-app |
| Tạo/lấy hàng/đang vận chuyển/đang giao | In-app khi caller shipment-events đã nối |

Các lựa chọn optional và trạng thái bật/tắt channel vẫn được tôn trọng; các loại/pair mandatory theo policy/template không bị tắt bằng preference cá nhân. Push mặc định không gửi trong chính sách mới; chỉ bật lại cho nhóm quan trọng bằng NotificationRouting:EnablePush=true và cấu hình provider tương ứng. OTP dùng worker riêng.

## Đã triển khai

- [x] User caller luôn dùng machine identity; loại bỏ danh tính/token/cookie caller bên ngoài và gắn khóa từ cấu hình server.
- [x] Commerce worker gắn khóa riêng server-configured; giữ nguyên retry và payload đã lưu.
- [x] Email/in-app chấp nhận khóa backend hợp lệ >=32 byte, giữ role fallback qua middleware hiện có; không chấp nhận chỉ vì request ở mạng nội bộ.
- [x] Giữ riêng Auction JWT và Wallet scoped key; không bypass hai hợp đồng này.
- [x] Planner Application được đăng ký bằng EF SaveChanges interceptor trong Infrastructure; chỉ áp dụng NotificationRecord Added có type, bỏ qua OTP/untyped. Không đọc lại lịch sử để gửi email hàng loạt.
- [x] Email và in-app được ghi cùng giao dịch với notification; Email Pending không được gọi là đã gửi.
- [x] Giữ source event/hash/receipt/idempotency; replay không tạo thêm delivery.
- [x] Receipt mật khẩu/Seller/account status chọn đúng delivery Email khi có thêm InApp, không nhầm ID dựa vào thứ tự GUID.
- [x] Email chưa có địa chỉ dùng reference user:<UUID>; worker tra cứu verified email qua REST có xác thực riêng. Thiếu người nhận ghi lỗi, service tra cứu lỗi được retry; không dùng email mặc định hoặc đọc DB User trực tiếp.
- [x] Nội dung in-app là text; giữ HTML email gốc. Email bổ sung dùng layout Nexus có escape nội dung và giới hạn kích thước.
- [x] Worker xử lý email có override dù notification không có TemplateId; quản trị có thể retry delivery thất bại theo version/request ID.
- [x] Bỏ nhánh legacy giả lập SMTP thành công khi thiếu cấu hình. Sau SMTP DATA acceptance, lỗi đóng kết nối không biến email thành chưa gửi.
- [x] Không xóa các alias FE đang dùng chỉ để khớp đề xuất trong báo cáo cũ.

## Kiểm chứng

| Kiểm tra | Kết quả |
|---|---|
| Build Notification/User Release | 0 lỗi, 0 warning |
| Toàn bộ test Notification | 901 passed, 0 failed, 0 skipped; sau sửa nhỏ bảo toàn text đã chạy lại 36/36 test phần ảnh hưởng |
| Toàn bộ test User | 592 passed, 0 failed, 0 skipped |
| Test caller Notification Commerce gồm PostgreSQL | 30 passed, 0 failed, 0 skipped |
| Docker User/Notification/Commerce | Build và health thành công |
| FE đăng ký, resend, mã cũ bị từ chối, xác minh mã mới | PASS với SMTP inbox fixture; Notification chạy Production |
| FE ban/unban tài khoản fixture mới | PASS: đúng lý do ở email và in-app |
| FE rút tiền duyệt/từ chối | PASS: đúng một thông báo mỗi event, email qua User contact lookup, trạng thái đọc và isolation |
| FE đặt hàng thường/thanh toán | PASS: HTTP 201 Paid, đúng một notice + một email cho mỗi Buyer/Seller |
| FE quên/reset/đổi mật khẩu | PASS: OTP, email xác nhận reset/đổi, đăng nhập mật khẩu mới và in-app reset trên SMTP fixture |
| Seller approve/reject với policy mới | Backend integration/replay PASS; chưa lặp lại hồ sơ thật trên FE trong lượt này |
| Các luồng Auction với policy mới | Ma trận kênh và API/auth/replay được kiểm thử; chưa tạo lại toàn bộ phiên đấu giá trên FE |

Các test tự động dùng DB PostgreSQL fixture riêng. SMTP inbox fixture không phải Gmail. Giai đoạn test tự động dùng hộp thư bắt mail để đối chiếu cả nội dung; đã khôi phục SMTP thật sau test, giữ nguyên private config nguồn. Chỉ đánh PASS hộp thư thật khi có người nhận xác nhận; SMTP ProviderAccepted/Sent là bằng chứng provider, không phải bằng chứng Inbox.

## Cấu hình và chạy lại

- NotificationRouting:Enabled mặc định true; false giữ đường dispatch trước thay đổi. Không dùng false để né kiểm chứng mới.
- NotificationRouting:EnablePush mặc định false.
- User gửi: Services:Notification:ServiceKey phải khớp INTERNAL_SERVICE_SECRET hoặc InternalSecurity:ServiceKey của Notification; khóa >=32 UTF-8 byte. Khóa dedicated sai không được tự bỏ qua để thử khóa khác.
- Commerce gửi: Notification:ServiceKey (hoặc Services:Notification:ServiceKey) khớp khóa backend Notification.
- Notification tra cứu người nhận: USER_SERVICE_URL hoặc Services:User:BaseUrl là địa chỉ User REST. Services:User:NotificationContactKey khớp NotificationContacts:ServiceKey phía User; nếu không đặt dedicated key, hai bên dùng khóa nội bộ hiện có. Header riêng X-Notification-Contact-Key không dựa vào danh tính trình duyệt và không được Gateway tự thêm.
- Các khóa/SMTP trong file private, không commit. Kiểm thử Production đã bắt được dedicated key User trong fixture lệch Notification; đã đồng bộ trong runtime fixture, không sửa file private nguồn.
- FE fixture http://localhost:3307; Gateway 29080; Notification 29007, User 29001, Commerce 29003. Không nhầm stack cũ 3107/3207 với stack này.

## Chưa hoàn tất tích hợp toàn dự án

- [ ] Fulfillment còn gọi delivery-alert không tồn tại. Cần snapshot shipment-events bền vững có event/version/IDs/timeline thật và retry; không chỉ đổi URL hoặc điền GUID/timestamp giả.
- [ ] Commerce bổ sung đầy đủ caller quyết định/hủy/hết hạn/hoàn tiền/hoàn tất/giải ngân theo hợp đồng tương ứng. Đặc biệt không thông báo cộng ví chỉ dựa vào cờ boolean hoặc Delivered.
- [ ] Bổ sung cảnh báo công việc tới Staff/Admin khi nguồn phát sự kiện và chỉ rõ recipient IDs/phạm vi quyền; không tự broadcast toàn bộ người dùng.
- [ ] Test lại toàn bộ Auction, Seller và giao hàng từ FE sau khi các caller trên sẵn sàng.
- [ ] Xác nhận nhận mail thật cho các nhóm quan trọng; cung cấp bằng chứng người nhận, không lưu OTP/mật khẩu/JWT/private key.

Xem hợp đồng bàn giao trong docs/contracts/NOTIFICATION_CALLER_GAPS_20261125.md. Các ô chưa đánh dấu là việc còn phải triển khai/kiểm chứng, không phải đã xong.

## Xác nhận Gmail thật

FE forgot-password đã tạo receipt 2ba9a19f-02e8-4792-a39f-fbe34f24de54, password_reset, ProviderAccepted, AttemptCount=1, ErrorCode trống, lúc 16:18 ngày 25/11/2026 (UTC+7). Người dùng xác nhận “Đã nhận thư mới”. Không đọc/lưu mã OTP. Đây là xác nhận OTP thật, không thay thế nghiệm thu Gmail của mọi nhóm email khác.
