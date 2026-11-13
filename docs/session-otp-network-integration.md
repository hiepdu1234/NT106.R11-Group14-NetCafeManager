# Tích hợp phiên sử dụng và OTP qua TCP

## Phạm vi

- Mỗi kết nối TCP giữ riêng máy đã handshake, người dùng đăng nhập và phiên sử dụng đang hoạt động.
- Client chỉ được mở phiên sau khi handshake máy và đăng nhập thành công.
- Server kiểm tra session token trước mọi thao tác phiên.
- Khi đăng xuất hoặc mất kết nối, Server cố gắng kết thúc phiên và cập nhật tiền trong SQL Server.
- Ba bước khôi phục mật khẩu được đưa qua TCP: gửi OTP, xác minh OTP và đặt lại mật khẩu.
- Đổi mật khẩu sẽ thu hồi các session token cũ như phần lõi xác thực đã thiết kế.

## Message type

| Message type | Dữ liệu gửi | Kết quả |
| --- | --- | --- |
| `MACHINE_HANDSHAKE_BEGIN` | `MachineHandshakeBeginRequest` | `SessionResult<MachineHandshakeChallenge>` |
| `MACHINE_HANDSHAKE_COMPLETE` | `MachineHandshakeCompleteRequest` | `SessionResult<VerifiedMachine>` |
| `AUTH_LOGIN` | `LoginRequest` | `LoginResponse` |
| `SESSION_START` | object rỗng | `SessionResult<UsageSessionSummary>` |
| `SESSION_GET_ACTIVE` | object rỗng | `UsageSessionSummary` hoặc `null` |
| `SESSION_END` | `EndUsageSessionRequest` | `SessionResult<UsageSessionSummary>` |
| `AUTH_LOGOUT` | object rỗng | kết quả đăng xuất |
| `AUTH_FORGOT_PASSWORD` | `RequestPasswordResetRequest` | `PasswordResetResponse` |
| `AUTH_VERIFY_OTP` | `VerifyPasswordResetOtpRequest` | `PasswordResetResponse` |
| `AUTH_RESET_PASSWORD` | `CompletePasswordResetRequest` | `PasswordResetResponse` |

## Cấu hình demo

Chạy `scripts/Generate-DemoEnvironment.ps1`, sau đó thay cấu hình SQL Server và SMTP bằng giá trị của môi trường demo. Server cần các biến:

- `NETCAFE_DB_CONNECTION`
- `NETCAFE_OTP_HASH_SECRET_BASE64`
- `NETCAFE_MACHINE_KEYS_JSON`
- `NETCAFE_HOURLY_RATE`
- `NETCAFE_SMTP_HOST`, `NETCAFE_SMTP_PORT`, `NETCAFE_SMTP_SECURITY`
- `NETCAFE_SMTP_USERNAME`, `NETCAFE_SMTP_PASSWORD`
- `NETCAFE_SMTP_FROM`, `NETCAFE_SMTP_FROM_NAME`
- `NETCAFE_TCP_PORT` nếu không dùng cổng mặc định `5050`

Không commit hoặc chụp màn hình secret, khóa máy, mật khẩu SMTP và OTP thật.

## Trình tự demo

1. Chạy Server và Client trên cùng máy.
2. Nhập tên máy và khóa Base64 được tạo cho môi trường demo.
3. Bấm **Kết nối và xác thực máy**.
4. Nhập tài khoản khách hàng, bấm **Đăng nhập và mở phiên**.
5. Bấm **Kết thúc phiên và đăng xuất** để Server tính tiền và trả máy về trạng thái trống.
6. Nhập tên đăng nhập hoặc email, chọn vai trò rồi lần lượt gửi OTP, xác minh OTP và đặt lại mật khẩu.

## Giới hạn an toàn

Server hiện chỉ lắng nghe `127.0.0.1`. Không đổi sang `0.0.0.0` hoặc IP LAN để gửi mật khẩu/OTP cho đến khi tầng TCP được bọc bằng TLS `SslStream` và Client xác minh chứng thư Server.

## Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.NetworkIntegrationSmoke/NetCafeManager.NetworkIntegrationSmoke.csproj --configuration Release
```

GitHub Actions `Check TCP session and OTP integration` chạy thêm smoke test với SQL Server cô lập.
