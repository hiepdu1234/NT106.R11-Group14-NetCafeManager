# Xác thực tài khoản

## Phạm vi hiện tại

- Hỗ trợ hai vai trò: `Administrator` và `Client`.
- Người chơi phải có tài khoản mới được đăng nhập và sử dụng máy.
- Server băm mật khẩu bằng PBKDF2-HMAC-SHA256 trước khi lưu.
- Mỗi tài khoản có salt ngẫu nhiên 32 byte riêng.
- Sau khi đăng nhập thành công, Server cấp session token ngẫu nhiên 32 byte, có hạn 8 giờ.
- Đăng xuất sẽ thu hồi session token.

## Cấu hình PBKDF2

Đồ án dùng 100.000 vòng lặp cho bản demo học thuật. Khi triển khai thực tế,
cần đo thời gian băm trên máy chủ và tăng số vòng phù hợp với cấu hình phần
cứng và chính sách bảo mật tại thời điểm triển khai.

Mật khẩu gốc không được ghi vào log, không lưu trong cơ sở dữ liệu và không
gửi lại cho Client. Chỉ lưu `Algorithm`, `Iterations`, `SaltBase64` và
`HashBase64` ở phía Server.

## Lưu ý khi tích hợp mạng và cơ sở dữ liệu

- Thay `InMemoryUserAccountRepository` bằng repository sử dụng SQL Server.
- Chỉ truyền thông tin đăng nhập qua kênh đã mã hóa bằng TLS/SslStream.
- Không ghi session token hoặc mật khẩu vào log.
- Khi Server khởi động lại, session trong bộ nhớ sẽ mất. Có thể bổ sung cơ chế
  đăng nhập lại hoặc kho session tập trung nếu phạm vi đồ án cần mở rộng.
