# Quản lý khách hàng

## Phạm vi

- Quản trị viên tạo tài khoản khách hàng mới.
- Mật khẩu được băm bằng PBKDF2-HMAC-SHA256, không lưu mật khẩu gốc.
- Xem danh sách, xem chi tiết và cập nhật email, họ tên, trạng thái hoạt động.
- Tài khoản mới luôn có vai trò `Client`, số dư bằng 0 và trạng thái hoạt động.
- Client không được gọi các nghiệp vụ quản trị khách hàng.

## Quy tắc chính

- Tên đăng nhập dài 3-50 ký tự, không chứa khoảng trắng.
- Email hợp lệ, không trùng với tài khoản khác.
- Mật khẩu dài 8-128 ký tự.
- Không xóa cứng tài khoản; dùng `IsActive = false` để khóa nhằm giữ lịch sử.

## Điểm tích hợp

Windows Forms gọi `CustomerManagementService` thông qua tầng xử lý thông điệp của
Server. Không cho Admin hoặc Client kết nối thẳng tới SQL Server.

Gói này độc lập với TCP/UDP. Khi nhánh mạng hoàn tất, chỉ cần ánh xạ DTO nhận từ
socket sang `CreateCustomerRequest` hoặc `UpdateCustomerRequest`.
