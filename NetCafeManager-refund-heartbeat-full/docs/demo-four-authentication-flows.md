# Chuẩn bị demo bốn cơ chế xác thực

## Mục tiêu

Gói này chỉ tạo dữ liệu và secret cho môi trường demo cục bộ. Không chứa mật khẩu, khóa máy, OTP secret hoặc mật khẩu SMTP trong Git.

Luồng demo gồm:

1. Đăng nhập tài khoản được băm PBKDF2 và đọc từ SQL Server.
2. Handshake máy `MAY-01` bằng challenge và HMAC proof.
3. Mở, kết thúc phiên sử dụng và cập nhật số dư.
4. Gửi OTP qua email, xác minh và đặt lại mật khẩu.

## 1. Chuẩn bị database

Tạo database `NetCafeManager`, sau đó chạy theo thứ tự trong SQL Server Management Studio:

1. `database/migrations/001_initial_schema.sql`
2. `database/seed/001_seed_core.sql`

Không chạy lại script tạo schema nếu database đã có đầy đủ bảng.

## 2. Sinh secret và thông tin demo

Mở PowerShell tại thư mục chứa `NetCafeManager.sln`:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\New-DemoSecrets.ps1 `
  -ClientEmail "email-client-cua-ban@example.com" `
  -AdminEmail "email-admin-cua-ban@example.com" `
  -SmtpHost "smtp.example.com" `
  -SmtpUsername "tai-khoan-smtp" `
  -SmtpFrom "email-gui@example.com"
```

Script sẽ yêu cầu nhập mật khẩu hoặc app password SMTP bằng ô nhập ẩn, rồi tạo `.demo-secrets.ps1`. File này đã được `.gitignore` loại khỏi Git.

Nạp các biến vào cửa sổ PowerShell hiện tại:

```powershell
. .\.demo-secrets.ps1
```

Không gửi hoặc chụp màn hình nội dung file này. Nếu dùng SMTP của nhà cung cấp có xác thực hai bước, sử dụng app password theo hướng dẫn của nhà cung cấp, không dùng mật khẩu tài khoản chính.

## 3. Tạo tài khoản PBKDF2

```powershell
dotnet run --project tools/NetCafeManager.DemoSetup
```

Lần chạy đầu sẽ tạo:

- `demo-client`, vai trò `Client`, số dư 50.000 đồng;
- `demo-admin`, vai trò `Administrator`;
- máy `MAY-01` ở trạng thái trống.

Mật khẩu ngẫu nhiên nằm trong `.demo-secrets.ps1` và không được in ra terminal. Nếu cần tạo lại đúng hai tài khoản demo, đổi dòng sau trong file secret rồi nạp lại:

```powershell
$env:NETCAFE_DEMO_REPLACE_EXISTING = 'true'
```

Không bật tùy chọn này cho tài khoản thật.

## 4. Chạy Server và Client

Terminal Server:

```powershell
. .\.demo-secrets.ps1
dotnet run --project src/NetCafeManager.Server
```

Terminal Client:

```powershell
dotnet run --project src/NetCafeManager.Client
```

Trong Client:

1. Nhập `MAY-01` và khóa máy lấy từ `NETCAFE_MACHINE_KEYS_JSON` trong file secret.
2. Bấm **Kết nối và xác thực máy**.
3. Nhập `demo-client` và mật khẩu demo, sau đó bấm **Đăng nhập và mở phiên**.
4. Bấm **Kết thúc phiên và đăng xuất**; kiểm tra số dư được cập nhật.
5. Nhập username hoặc email, chọn vai trò `Client`, bấm **Gửi OTP**.
6. Lấy OTP trong email, bấm **Xác minh OTP**, nhập mật khẩu mới rồi bấm **Đặt lại mật khẩu**.
7. Đăng nhập lại bằng mật khẩu mới.

## 5. Kiểm tra trước khi trình bày

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tools/NetCafeManager.DemoSetup/NetCafeManager.DemoSetup.csproj --configuration Release
```

Chỉ demo trên `127.0.0.1`. Không mở Server ra LAN khi chưa bổ sung TLS và xác minh chứng thư.
