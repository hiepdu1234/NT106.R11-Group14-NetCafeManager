# Xác thực SQL Server và khôi phục mật khẩu bằng OTP

## Phạm vi đã cài đặt

- `SqlServerUserAccountRepository` đọc tài khoản từ `TAI_KHOAN` cho luồng
  đăng nhập PBKDF2 hiện có.
- Hai vai trò hợp lệ là `Administrator` và `Client`; tài khoản bị khóa không
  được đăng nhập hoặc đặt lại mật khẩu.
- OTP gồm 6 chữ số, có hiệu lực 5 phút và bị khóa sau 5 lần nhập sai.
- OTP không được lưu nguyên văn. Server lưu HMAC-SHA256 32 byte tạo từ mã OTP,
  mã tài khoản và secret riêng của Server.
- Yêu cầu gửi OTP luôn trả cùng một thông báo, kể cả tài khoản không tồn tại,
  để không làm lộ danh sách tài khoản.
- Sau khi đổi mật khẩu, OTP bị vô hiệu hóa và toàn bộ session đang tồn tại của
  tài khoản bị thu hồi.

## Cấu hình Server

Server phải tạo `AuthenticationServices` đúng một lần và tái sử dụng trong
suốt thời gian chạy:

```csharp
byte[] otpHashSecret = Convert.FromBase64String(
    Environment.GetEnvironmentVariable("NETCAFE_OTP_HASH_SECRET_BASE64")
    ?? throw new InvalidOperationException("Missing OTP hash secret."));

AuthenticationServices services = AuthenticationSetup.CreateServicesForSqlServer(
    connectionString,
    passwordResetCodeSender,
    otpHashSecret);
```

`NETCAFE_OTP_HASH_SECRET_BASE64` phải chứa ít nhất 32 byte ngẫu nhiên sau khi
giải mã. Không đưa secret, mật khẩu, session token hoặc OTP vào source code,
commit, log hay thông báo lỗi.

`IPasswordResetCodeSender` tách việc tạo/xác minh OTP khỏi SMTP.
`MailKitPasswordResetCodeSender` là adapter gửi email thật. Nhóm cần tạo
`SmtpOptions` từ biến môi trường; tuyệt đối không ghi username/password SMTP
trực tiếp vào source code hoặc commit.

## Thông điệp mạng cần nối sau

Phần lõi tương ứng với bốn message type trong tài liệu thiết kế:

| Message type | Hàm Server |
| --- | --- |
| `AUTH_LOGIN` | `AuthenticationService.LoginAsync` |
| `AUTH_FORGOT_PASSWORD` | `PasswordResetService.RequestAsync` |
| `AUTH_VERIFY_OTP` | `PasswordResetService.VerifyOtpAsync` |
| `AUTH_RESET_PASSWORD` | `PasswordResetService.CompleteAsync` |

Các hàm này đã được nối vào TCP handler và giao diện WinForms demo bằng các
message type tương ứng. Bản hiện tại chỉ lắng nghe loopback; không mở ra LAN
cho đến khi bổ sung TLS và xác minh chứng thư Server.

## Kiểm thử

Workflow `Check SQL Server authentication and OTP` tạo database tách biệt
`NetCafeManager_CI`, áp dụng schema/seed và kiểm tra:

- ánh xạ vai trò và đăng nhập bằng PBKDF2;
- từ chối sai mật khẩu, tài khoản khóa và tài khoản không tồn tại;
- phản hồi chung cho yêu cầu OTP không tồn tại;
- xác minh OTP đúng/sai, giới hạn 5 lần và hết hạn 5 phút;
- đổi mật khẩu, chặn dùng lại OTP và thu hồi session cũ.

Không chạy smoke test bằng chuỗi kết nối tới database thật.

## Trạng thái bốn cơ chế xác thực

1. PBKDF2 cho tài khoản: đã có và đã nối SQL Server.
2. Handshake định danh máy trạm: đã nối vào ngữ cảnh từng kết nối TCP.
3. Xác thực phiên chơi theo máy, số dư và đăng nhập đồng thời: đã nối TCP;
   tự kết thúc phiên khi đăng xuất hoặc mất kết nối có kiểm soát.
4. Khôi phục mật khẩu OTP: đã có lõi Server, SMTP, TCP handler, giao diện
   WinForms demo và smoke test.
