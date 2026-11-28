# Yêu cầu cho UserService

## Đã thấy trên `origin/dev`

Client gọi Notification cho OTP, thay đổi mật khẩu, quyết định Seller và ban/unban. Đã có kiểm chứng local trước đây về email OTP và Seller, nhưng đó không thay thế test lại sau khi merge `dev` và chạy server. Merge hiện xung đột tại `User.Infrastructure/Services/Notification/EmailNotificationClient.cs`; code local được giữ nguyên.

## Cần phía User

1. Giải conflict client bằng đối chiếu contract Notification mới nhất: registration/password-reset/seller-registration OTP, `password-changes`, `seller-decisions`, `account-status-changes`. Không để caller cũ và mới cùng gửi một thư.
2. OTP chỉ gửi cho request còn hiệu lực; resend tạo mã và request ID mới theo chính sách, retry của cùng request giữ ID. Ghi rõ phản hồi FE là “đã tiếp nhận” hay provider đã chấp nhận; không lộ tài khoản tồn tại ở quên mật khẩu.
3. Quyết định Seller và ban/unban sau commit cần audit ID/sourceEventId bền vững, email người nhận chuẩn, lý do từ chối/khóa/mở khóa rõ ràng; lỗi Notification không đảo ngược trạng thái User nhưng phải có retry/đối soát.
4. Xác nhận đổi/reset mật khẩu không chứa mật khẩu/OTP trong thư và log. Kiểm tra token/phiên cũ theo chính sách hiện tại.

**PASS:** đăng ký → resend → xác minh; quên mật khẩu → reset; duyệt/từ chối Seller; ban/unban qua FE. Đối chiếu DB User, Notification và thư thật; mã cũ bị từ chối sau resend.
