# Nền tảng cơ sở dữ liệu

## Phạm vi

- SQL Server và Entity Framework Core 8.
- Chỉ có hai vai trò: `Administrator` và `Client`.
- Không có bảng nhân viên, mở ca hoặc đóng ca.
- Mật khẩu dùng đúng định dạng của phần xác thực hiện tại:
  `PasswordAlgorithm`, `IterationCount`, `PasswordSaltBase64` và
  `PasswordHashBase64`.
- Mã tài khoản dùng `UNIQUEIDENTIFIER` để khớp với `Guid` trong mã xác thực
  đã được gộp vào `main`.

## Các bảng khởi tạo

1. `TAI_KHOAN`
2. `MAY_TINH`
3. `PHIEN_SU_DUNG`
4. `DICH_VU`
5. `CHI_TIET_DICH_VU`
6. `HOA_DON`
7. `GOI_GIO`
8. `GIAO_DICH_NAP`
9. `PASSWORD_RESET_OTP`
10. `LENH_DIEU_KHIEN`
11. `CHINH_SACH_CHAN`
12. `VI_PHAM_CHINH_SACH`

## Quy tắc tồn kho

- `SanPham` phải có `SoLuongTon >= 0` và `MucTonChuan > 0`.
- Trạng thái `Ít` được tính khi số lượng còn lại nhỏ hơn 10% mức tồn chuẩn.
- `NguQuaDem` không quản lý tồn kho nên hai cột này để `NULL`.
- Không có cột Ngừng bán/Bán lại theo quyết định phạm vi hiện tại.

## Cách tạo cơ sở dữ liệu bằng SQL

1. Tạo database `NetCafeManager` trong SQL Server.
2. Chọn database vừa tạo.
3. Chạy `database/migrations/001_initial_schema.sql`.
4. Chạy `database/seed/001_seed_core.sql`.

Dữ liệu seed chỉ gồm gói giờ, máy mẫu, sản phẩm và dịch vụ mẫu. Không tạo sẵn
tài khoản có mật khẩu để tránh lưu mật khẩu demo yếu trong repository.

## Cách tạo migration bằng EF Core

Đặt chuỗi kết nối trong biến môi trường:

```powershell
$env:NETCAFE_DB_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Database=NetCafeManager;Trusted_Connection=True;TrustServerCertificate=True"
```

Sau đó chạy tại thư mục gốc repository:

```powershell
dotnet restore
dotnet ef migrations add InitialCreate --project src/NetCafeManager.Infrastructure --startup-project src/NetCafeManager.Server --output-dir Persistence/Migrations
dotnet ef database update --project src/NetCafeManager.Infrastructure --startup-project src/NetCafeManager.Server
```

## Kiểm tra

```powershell
dotnet build NetCafeManager.sln
```

Chỉ đánh dấu hoàn thành khi build không lỗi và cả migration lẫn seed chạy được
trên một database mới.
