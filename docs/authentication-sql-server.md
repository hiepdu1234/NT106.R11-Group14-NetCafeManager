# Xác thực tài khoản với SQL Server

`SqlServerUserAccountRepository` đọc tài khoản từ bảng `TAI_KHOAN` cho
`AuthenticationService`. Repository không thay đổi schema, thuật toán PBKDF2
hay quy tắc phiên hiện tại. Hai vai trò được chấp nhận là `Administrator` và
`Client`. Tài khoản bị khóa hoặc không tồn tại không được cấp phiên.

## Kiểm thử

CI tạo database riêng `NetCafeManager_CI`, chạy schema/seed rồi chạy:

```powershell
dotnet run --project tests/NetCafeManager.AuthenticationSmoke --configuration Release
```

Nếu chạy thủ công, đặt `NETCAFE_AUTH_SMOKE_CONNECTION` bằng chuỗi kết nối tới
database kiểm thử nói trên đã khởi tạo schema. Không dùng database thật. Mã
kiểm thử tự sinh tài khoản và mật khẩu tạm, xóa các tài khoản đó sau khi chạy,
và không in mật khẩu hoặc token.

## Phạm vi hiện tại

- **Mật khẩu PBKDF2:** nối tầng xác thực với SQL Server. Giữ định dạng PBKDF2
  đang có trên `main` (salt 32 byte, 100.000 vòng cho demo); tài liệu Giai
  đoạn 2 ghi salt 16 byte, cần thống nhất lại trước khi đổi định dạng.
- **Định danh máy trạm:** chưa làm `MACHINE_HANDSHAKE`. Cần phối hợp với nhánh
  TCP của Quân để chốt giao thức, cấp khóa và chống phát lại.
- **Phiên khách:** đã kiểm tra vòng đời token có sẵn, nhưng chưa có phiên chơi
  gắn với máy/kết nối, chống đăng nhập song song, kiểm tra số dư hay chốt tiền.
- **OTP khôi phục:** chưa làm luồng gửi/xác minh/đặt lại mật khẩu. Đây là luồng
  riêng cho Admin và Client, không phải một bước trong mỗi lần đăng nhập.

`AuthenticationSetup.CreateForSqlServer` dùng để khởi tạo dịch vụ ở Server,
không phải API mạng. Khi tích hợp, chỉ gửi mật khẩu qua kết nối đã mã hóa và
chỉ cấp phiên chơi sau khi Server kiểm tra đủ điều kiện của khách và máy.

## Đưa code lên GitHub Desktop

1. Nếu PR #3 đã merge, cập nhật `main` bằng Fetch/Pull và tạo nhánh mới từ
   `main`. Nếu chưa merge, cập nhật nhánh `feature/database-foundation` và tạo
   nhánh từ đó. Đặt tên nhánh mới là `feature/authentication-sql`.
2. Giải nén ZIP bên ngoài repository. Chép nội dung `.github`, `docs`, `src`
   và `tests` vào thư mục gốc có `NetCafeManager.sln`. Không chép chính ZIP vào
   repository. Bản này chỉ thêm file; nếu có cảnh báo ghi đè, kiểm tra trước.
3. Build solution trên máy, rồi xem danh sách thay đổi trong GitHub Desktop.
   Commit: `Thêm: Nối xác thực tài khoản với SQL Server`.
4. Bấm **Publish branch** (nhánh mới) hoặc **Push origin** (nhánh đã publish).
   Mở **Actions → Check SQL Server authentication** để xem build và test.
   Nếu PR #3 chưa merge, chưa mở PR phần này vào `main` để tránh lẫn diff.
