# Handshake máy trạm và phiên sử dụng

## Phạm vi đã cài đặt

- Server cấp challenge ngẫu nhiên 32 byte, có hạn 2 phút.
- Máy trạm tạo proof HMAC-SHA256 bằng khóa riêng tối thiểu 32 byte.
- Challenge bị thu hồi ngay khi xác minh, vì vậy không thể phát lại proof cũ.
- Máy bị khóa, bảo trì hoặc gặp sự cố không được handshake.
- Kết quả handshake có hạn 8 giờ và được truyền cho nghiệp vụ phiên sử dụng.
- Một tài khoản chỉ có một phiên đang hoạt động và một máy chỉ phục vụ một tài khoản.
- Tài khoản bị khóa hoặc hết tiền không được bắt đầu phiên.
- Khi kết thúc phiên, Server tính phí theo phút, cập nhật số dư và trả máy về trạng thái trống trong cùng transaction.

## Điểm tích hợp dành cho nhánh TCP

TCP handler chỉ cần giữ `VerifiedMachine` trong ngữ cảnh của kết nối:

1. Nhận tên máy và gọi `MachineHandshakeService.BeginAsync`.
2. Gửi `MachineHandshakeChallenge` về Client.
3. Nhận proof và gọi `MachineHandshakeService.CompleteAsync`.
4. Sau đăng nhập, gọi `UsageSessionService.StartAsync(user, verifiedMachine)`.
5. Khi đăng xuất hoặc ngắt phiên có kiểm soát, gọi `UsageSessionService.EndAsync`.

Không nhận `ComputerId` hoặc đơn giá giờ từ Client. Mã máy được lấy từ kết quả
handshake, còn đơn giá được cấu hình tại Server.

## Biến môi trường demo

- `NETCAFE_DB_CONNECTION`: chuỗi kết nối SQL Server.
- `NETCAFE_OTP_HASH_SECRET_BASE64`: secret HMAC cho OTP, tối thiểu 32 byte.
- `NETCAFE_MACHINE_KEYS_JSON`: JSON ánh xạ tên máy sang khóa Base64.
- `NETCAFE_HOURLY_RATE`: đơn giá giờ do Server quản lý.
- `NETCAFE_SMTP_HOST`, `NETCAFE_SMTP_PORT`, `NETCAFE_SMTP_USERNAME`,
  `NETCAFE_SMTP_PASSWORD`, `NETCAFE_SMTP_FROM`: cấu hình gửi OTP.

Chạy `scripts/Generate-DemoEnvironment.ps1` để tạo secret ngẫu nhiên cho OTP và
hai máy mẫu. Không chụp màn hình, commit hoặc ghi secret thật vào tài liệu.

## Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.SessionSmoke/NetCafeManager.SessionSmoke.csproj --configuration Release
```

Smoke test SQL Server kiểm tra proof sai, chống replay, ánh xạ máy, chặn một tài
khoản dùng hai máy, chặn hai tài khoản dùng một máy, kiểm tra số dư, tính phí và
không tính tiền hai lần khi kết thúc phiên.

## Chưa thuộc gói này

- Không sửa `Program.cs`, TCP listener, framing hoặc message handler.
- Không sửa giao diện Client/Admin WinForms.
- Việc tự động kết thúc ngay khi số dư cạn cần bộ lập lịch của Server sau khi
  nhánh TCP và vòng đời kết nối được hợp nhất.
