# TCP và giao diện quản trị hoàn chỉnh

## Phạm vi đã tích hợp

Ứng dụng `NetCafeManager.Client` dùng màn hình đăng nhập chung. Khi thiết bị
được cấu hình là **Máy quản trị** và Server xác thực tài khoản có vai trò
`Administrator`, ứng dụng mở bảng điều khiển gồm:

- Khách hàng: tải danh sách, tạo, sửa, khóa/mở khóa tài khoản.
- Nạp tiền: chọn gói giờ, cộng số dư và xem lịch sử nạp.
- Hoàn tiền: hoàn toàn bộ số dư, đóng tài khoản và xem lịch sử hoàn.
- Gói giờ: tạo, sửa giá/số giờ và bật/tắt áp dụng.
- Kho/sản phẩm: tạo, sửa, nhập/xuất tồn kho và xóa mục chưa phát sinh giao dịch.
- Hóa đơn: lập từ phiên đã kết thúc, xem danh sách và xác nhận thanh toán.
- Máy trạm: xem trạng thái/heartbeat, đổi trạng thái, khóa hoặc đăng xuất từ xa,
  xem lịch sử trạng thái và lịch sử lệnh.
- Nhật ký quản trị: xem các thao tác đổi trạng thái và xếp lệnh điều khiển.

Mọi lệnh `ADMIN_*` đều đi qua TCP. Server xác minh token và vai trò quản trị
trước khi gọi service; các service vẫn kiểm tra quyền lần thứ hai.

## Cảnh báo và tự kết thúc khi hết tiền

Mỗi heartbeat của máy khách yêu cầu Server tính chi phí phiên đến thời điểm
hiện tại:

1. Khi số dư ước tính chỉ còn tối đa 15 phút, Client hiển thị cảnh báo một lần.
2. Khi chi phí đạt hoặc vượt số dư, Server kết thúc phiên với lý do `HetTien`,
   trừ tối đa số dư hiện có và trả máy về trạng thái trống.
3. Client nhận trạng thái trong heartbeat, thông báo cho người dùng và quay về
   màn hình đăng nhập.

Tính tiền và kết thúc phiên vẫn được thực hiện trong transaction có khóa ứng
dụng SQL Server; heartbeat không tự trừ tiền từng nhịp.

## Chạy demo trên một máy

Không đặt `NETCAFE_LISTEN_ADDRESS` và không đặt biến chứng thư. Server mặc định
lắng nghe `127.0.0.1:5050` không TLS. Trên Desktop nhập:

- Server: `127.0.0.1`
- Cổng: `5050`
- Bỏ chọn **Bật TLS**

## Chạy qua mạng LAN bằng TLS

### 1. Tạo chứng thư trên máy Server

Mở PowerShell với quyền người dùng đang chạy Server:

```powershell
$password = Read-Host "Mật khẩu bảo vệ PFX" -AsSecureString
.\scripts\New-LanTlsCertificate.ps1 `
  -DnsName "TEN-MAY-SERVER" `
  -OutputPath ".\secrets\netcafe-server.pfx" `
  -Password $password
```

Lưu giá trị SHA-256 mà script in ra. Không commit tệp `.pfx` hoặc mật khẩu vào
Git.

### 2. Cấu hình Server

```powershell
$env:NETCAFE_LISTEN_ADDRESS = "0.0.0.0"
$env:NETCAFE_TCP_PORT = "5050"
$env:NETCAFE_TLS_CERTIFICATE_PATH = "C:\duong-dan\netcafe-server.pfx"
$env:NETCAFE_TLS_CERTIFICATE_PASSWORD = "mat-khau-pfx"
dotnet run --project .\src\NetCafeManager.Server
```

Server từ chối khởi động trên địa chỉ không phải loopback nếu chưa có chứng thư
TLS chứa private key.

### 3. Cấu hình Desktop

- Nhập IP hoặc tên máy Server trong LAN.
- Chọn **Bật TLS**.
- Dán SHA-256 của chứng thư vào ô dấu vân tay.
- Lưu cấu hình và thực hiện handshake máy như bình thường.

Desktop xác minh SHA-256 bằng so sánh thời gian cố định. Nếu bỏ trống dấu vân
tay, chứng thư phải hợp lệ theo kho tin cậy của Windows và đúng tên Server.

## Kiểm tra trước khi merge

```powershell
dotnet build .\NetCafeManager.sln --configuration Release -p:BuildInParallel=false
dotnet run --project .\tests\NetCafeManager.AdminOperationsSmoke --configuration Release
dotnet run --project .\tests\NetCafeManager.SessionSmoke --configuration Release
dotnet run --project .\tests\NetCafeManager.NetworkResilienceSmoke --configuration Release
dotnet run --project .\tests\NetCafeManager.ClientReconnectSmoke --configuration Release
dotnet run --project .\tests\NetCafeManager.RemoteMachineControlSmoke --configuration Release
```

Hai smoke test dùng SQL Server cần biến kết nối tương ứng như mô tả trong các
workflow `check-admin-operations.yml` và `check-machine-session.yml`.
