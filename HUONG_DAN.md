<<<<<<< Updated upstream
# Hướng dẫn các gói chức năng của Tuấn Hiệp
=======
# Gói 03 - Lập hóa đơn
>>>>>>> Stashed changes

## Gói 01 - Quản lý khách hàng

### Nhánh đề xuất

`feature/invoice-service`

### Điều kiện trước khi áp dụng

- Nhánh OTP đã merge vào `main`.
- Cơ sở dữ liệu nền tảng đã có bảng phiên, chi tiết dịch vụ và hóa đơn.

### Cách áp dụng

Chép `.github`, `docs`, `src`, `tests` vào thư mục chứa `NetCafeManager.sln`.

### Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
<<<<<<< Updated upstream
dotnet build tests/NetCafeManager.CustomerManagementSmoke/NetCafeManager.CustomerManagementSmoke.csproj --configuration Release
=======
dotnet build tests/NetCafeManager.InvoiceSmoke --configuration Release
>>>>>>> Stashed changes
```

### Commit đề xuất

<<<<<<< Updated upstream
`Thêm: Quản lý tài khoản khách hàng`

## Gói 02 - Nạp tiền, số dư và gói giờ

### Nhánh đề xuất

`feature/balance-topup-hour-packages`

### Điều kiện trước khi áp dụng

- Nhánh OTP và gói quản lý khách hàng đã merge vào `main`.
- Đã chuyển về `main` và Fetch/Pull origin.
- Tạo nhánh mới từ `main`.

### Cách áp dụng

Chép `.github`, `docs`, `src`, `tests` vào thư mục chứa `NetCafeManager.sln`,
sau đó chọn ghi đè/ghép thư mục khi Windows hỏi.

### Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.BillingSmoke/NetCafeManager.BillingSmoke.csproj --configuration Release
```

### Commit đề xuất

`Thêm: Quản lý số dư, nạp tiền và gói giờ`

### Phần đang chờ Minh Quân

- Tự động trừ tiền theo phiên sử dụng máy.
- Gửi cảnh báo qua TCP khi tài khoản sắp hết tiền.
- Khóa hoặc đăng xuất máy trạm khi phiên kết thúc hoặc hết tiền.
=======
`Thêm: Lập và cập nhật trạng thái hóa đơn`

## Phần đang chờ Quân

Điểm gọi tự động khi kết thúc phiên. Code hiện yêu cầu phiên đã mang trạng thái
`KetThuc`, nhưng không sửa hoặc giả lập module TCP/phiên sử dụng.
>>>>>>> Stashed changes
