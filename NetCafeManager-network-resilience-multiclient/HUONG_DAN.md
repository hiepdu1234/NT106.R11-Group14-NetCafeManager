# Hướng dẫn các gói chức năng của Tuấn Hiệp

## Quy trình chung

1. Chuyển về `main`, nhấn Fetch/Pull origin và tạo nhánh mới từ `main`.
2. Chép các thư mục trong ZIP vào thư mục chứa `NetCafeManager.sln`.
3. Build solution và smoke test của gói hiện tại.
4. Commit, push, tạo Pull Request và chỉ merge khi các checks đã xanh.

## Gói 01 - Quản lý khách hàng

### Nhánh

`feature/customer-management`

### Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.CustomerManagementSmoke/NetCafeManager.CustomerManagementSmoke.csproj --configuration Release
```

### Commit

`Thêm: Quản lý tài khoản khách hàng`

## Gói 02 - Nạp tiền, số dư và gói giờ

### Nhánh

`feature/balance-topup-hour-packages`

### Điều kiện

- Gói quản lý khách hàng đã merge vào `main`.
- Phần tự động trừ tiền theo phiên, cảnh báo qua TCP và khóa máy đang chờ module phiên sử dụng của Quân.

### Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.BillingSmoke/NetCafeManager.BillingSmoke.csproj --configuration Release
```

### Commit

`Thêm: Quản lý số dư, nạp tiền và gói giờ`

## Gói 03 - Lập hóa đơn

### Nhánh

`feature/invoice-service`

### Điều kiện

- Cơ sở dữ liệu đã có bảng phiên, chi tiết dịch vụ và hóa đơn.
- Điểm gọi tự động khi kết thúc phiên đang chờ module phiên sử dụng của Quân.

### Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.InvoiceSmoke/NetCafeManager.InvoiceSmoke.csproj --configuration Release
```

### Commit

`Thêm: Chức năng lập và quản lý hóa đơn`

## Hotfix hóa đơn

### Nhánh

`fix/invoice-concurrency-and-guide`

### Nội dung

- Xóa toàn bộ dấu conflict còn sót trong tài liệu này.
- Khóa theo mã phiên khi lập hóa đơn để các yêu cầu đồng thời trả về cùng một hóa đơn.
- Cập nhật trạng thái bằng câu lệnh nguyên tử để hóa đơn đã thanh toán không bị đổi lại thành công nợ.
- Bổ sung smoke test cho hai tình huống đồng thời trên.

### Commit

`Sửa: Đồng bộ hóa đơn và làm sạch hướng dẫn`
# Hướng dẫn các gói chức năng của Tuấn Hiệp

## Quy trình chung

1. Chuyển về `main`, nhấn Fetch/Pull origin và tạo nhánh mới từ `main`.
2. Chép các thư mục trong ZIP vào thư mục chứa `NetCafeManager.sln`.
3. Build solution và smoke test của gói hiện tại.
4. Commit, push, tạo Pull Request và chỉ merge khi các checks đã xanh.

## Gói 01 - Quản lý khách hàng

### Nhánh

`feature/customer-management`

### Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.CustomerManagementSmoke/NetCafeManager.CustomerManagementSmoke.csproj --configuration Release
```

### Commit

`Thêm: Quản lý tài khoản khách hàng`

## Gói 02 - Nạp tiền, số dư và gói giờ

### Nhánh

`feature/balance-topup-hour-packages`

### Điều kiện

- Gói quản lý khách hàng đã merge vào `main`.
- Phần tự động trừ tiền theo phiên, cảnh báo qua TCP và khóa máy đang chờ module phiên sử dụng của Quân.

### Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.BillingSmoke/NetCafeManager.BillingSmoke.csproj --configuration Release
```

### Commit

`Thêm: Quản lý số dư, nạp tiền và gói giờ`

## Gói 03 - Lập hóa đơn

### Nhánh

`feature/invoice-service`

### Điều kiện

- Cơ sở dữ liệu đã có bảng phiên, chi tiết dịch vụ và hóa đơn.
- Điểm gọi tự động khi kết thúc phiên đang chờ module phiên sử dụng của Quân.

### Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.InvoiceSmoke/NetCafeManager.InvoiceSmoke.csproj --configuration Release
```

### Commit

`Thêm: Chức năng lập và quản lý hóa đơn`

## Hotfix hóa đơn

### Nhánh

`fix/invoice-concurrency-and-guide`

### Nội dung

- Xóa toàn bộ dấu conflict còn sót trong tài liệu này.
- Khóa theo mã phiên khi lập hóa đơn để các yêu cầu đồng thời trả về cùng một hóa đơn.
- Cập nhật trạng thái bằng câu lệnh nguyên tử để hóa đơn đã thanh toán không bị đổi lại thành công nợ.
- Bổ sung smoke test cho hai tình huống đồng thời trên.

### Commit

`Sửa: Đồng bộ hóa đơn và làm sạch hướng dẫn`
