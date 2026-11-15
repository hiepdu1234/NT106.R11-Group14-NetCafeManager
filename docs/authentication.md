# Xác thực tài khoản

## Phạm vi hiện tại

- Hỗ trợ hai vai trò: `Administrator` và `Client`.
- Người chơi phải có tài khoản mới được đăng nhập và sử dụng máy.
- Server băm mật khẩu bằng PBKDF2-HMAC-SHA256 trước khi lưu.
- Mỗi tài khoản có salt ngẫu nhiên 32 byte riêng.
- Sau khi đăng nhập thành công, Server cấp session token ngẫu nhiên 32 byte, có hạn 8 giờ.
- Đăng xuất sẽ thu hồi session token.

## Cấu hình PBKDF2

Mặc định Server dùng 600.000 vòng lặp cho PBKDF2-HMAC-SHA256. Có thể thay đổi
bằng biến môi trường `NETCAFE_PBKDF2_ITERATIONS` trong khoảng 100.000 đến
10.000.000 sau khi đo thời gian băm trên máy chủ. Tài khoản cũ dùng 100.000
vòng vẫn đăng nhập được và được nâng cấp tự động sau lần đăng nhập thành công.

Mật khẩu gốc không được ghi vào log, không lưu trong cơ sở dữ liệu và không
gửi lại cho Client. Chỉ lưu `Algorithm`, `Iterations`, `SaltBase64` và
`HashBase64` ở phía Server.

## Lưu ý khi tích hợp mạng và cơ sở dữ liệu

- Server chính sử dụng `SqlServerUserAccountRepository`; repository in-memory
  chỉ dành cho kiểm thử.
- Chỉ truyền thông tin đăng nhập qua kênh đã mã hóa bằng TLS/SslStream.
- Không ghi session token hoặc mật khẩu vào log.
- Khi Server khởi động lại, session trong bộ nhớ sẽ mất. Có thể bổ sung cơ chế
  đăng nhập lại hoặc kho session tập trung nếu phạm vi đồ án cần mở rộng.
