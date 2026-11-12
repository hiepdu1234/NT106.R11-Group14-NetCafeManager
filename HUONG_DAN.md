# Gói 01 - Quản lý khách hàng

## Nhánh đề xuất

`feature/customer-management`

## Điều kiện trước khi áp dụng

- Nhánh OTP đã merge vào `main`.
- Đã chuyển về `main` và Fetch/Pull origin.
- Tạo nhánh mới từ `main`.

## Cách áp dụng

Chép `.github`, `docs`, `src`, `tests` vào thư mục chứa `NetCafeManager.sln`,
sau đó chọn ghi đè/ghép thư mục khi Windows hỏi.

## Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.CustomerManagementSmoke --configuration Release
```

## Commit đề xuất

`Thêm: Quản lý tài khoản khách hàng`
