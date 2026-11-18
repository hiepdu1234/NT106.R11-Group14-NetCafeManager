# Quản lý sản phẩm, dịch vụ và tồn kho

## Phạm vi

- Quản trị viên tạo và cập nhật sản phẩm hoặc dịch vụ ngủ qua đêm.
- Client đọc danh sách đã lưu trên Server.
- Sản phẩm quản lý đơn giá, đơn vị tính, số lượng tồn và mức tồn chuẩn.
- Tồn kho dưới 10% mức chuẩn được đánh dấu `It`; từ 10% trở lên là `Nhieu`.
- Điều chỉnh nhập/xuất kho được khóa theo mã sản phẩm để tránh mất cập nhật đồng thời.
- Không cho xuất vượt tồn kho và không xóa mục đã phát sinh sử dụng.

## Phân quyền

- Administrator: tạo, sửa, xóa và điều chỉnh tồn kho.
- Client: chỉ được xem danh sách.

## Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.InventorySmoke/NetCafeManager.InventorySmoke.csproj --configuration Release
```

GitHub Actions chạy smoke test trên SQL Server 2022 để kiểm tra phân quyền, cảnh báo tồn thấp, nhập/xuất kho và xóa dịch vụ chưa được sử dụng.
