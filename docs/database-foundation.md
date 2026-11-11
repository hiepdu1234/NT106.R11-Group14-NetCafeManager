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

## EF Core migration

`NetCafeDbContext` và design-time factory đã được viết, nhưng chưa commit
migration do EF Core sinh ra. CI chỉ tạo migration tạm thời và xuất SQL để kiểm
tra mô hình; các tệp tạm này không được tự động đưa vào repository.

Nếu nhóm quyết định dùng EF migration thay script SQL ở trên, hãy tạo database
mới riêng, cài CLI và sinh migration:

```powershell
dotnet tool install --global dotnet-ef --version 8.0.33
dotnet ef migrations add InitialCreate --project src/NetCafeManager.Infrastructure --startup-project src/NetCafeManager.Infrastructure --output-dir Persistence/Migrations
```

Sau khi review và commit migration, đặt chuỗi kết nối bằng biến môi trường:

```powershell
$env:NETCAFE_DB_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Database=NetCafeManager;Trusted_Connection=True;TrustServerCertificate=True"
```

Chỉ chạy `dotnet ef database update --project src/NetCafeManager.Infrastructure --startup-project src/NetCafeManager.Infrastructure`
trên database mới nếu chọn hướng EF. **Không chạy cả script tạo bảng và EF
database update trên cùng một database**, vì cả hai cùng tạo schema ban đầu.

## Kiểm tra

```powershell
dotnet restore
dotnet build NetCafeManager.sln
```

CI Windows kiểm tra build và sinh migration tạm. Vẫn cần chạy schema SQL và seed
trên một SQL Server thật trước khi đánh dấu hoàn thành và tạo Pull Request.
