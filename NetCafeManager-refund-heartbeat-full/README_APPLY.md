# Cách áp dụng bằng GitHub Desktop

Gói này chứa 12 tệp đã sửa đầy đủ cho nhánh
`fix/business-data-hardening`.

## Áp dụng

1. Trong GitHub Desktop, xác nhận **Current branch** là
   `fix/business-data-hardening`.
2. Mở thư mục repository bằng **Repository > Show in Explorer**.
3. Sao chép toàn bộ thư mục con trong gói này vào thư mục gốc repository và chọn
   **Replace the files in the destination**.
4. Không thay hoặc khôi phục `database/migrations/001_initial_schema.sql`; tệp đó
   không nằm trong gói vì đang có thay đổi riêng của bạn.
5. Quay lại GitHub Desktop, kiểm tra danh sách thay đổi trước khi commit.

## Nội dung chính

- Bỏ trạng thái `CongNo`, giữ mô hình trả trước.
- Xác nhận hóa đơn không trừ số dư lần hai và có tính idempotent.
- Tạm khóa giảm giá khác 0 cho đến khi tích hợp vào transaction tính tiền.
- Thêm `sp_getapplock` cho nạp tiền đồng thời.
- Thêm migration `003_prepaid_invoice_policy.sql`.
- Cập nhật smoke test hóa đơn và nạp tiền đồng thời.
- Sửa ba workflow để chạy migration 001 -> 002 -> 003 -> seed.

## Kiểm tra trước khi push

```powershell
dotnet build src/NetCafeManager.Server/NetCafeManager.Server.csproj --configuration Release --no-restore
dotnet build tests/NetCafeManager.InvoiceSmoke/NetCafeManager.InvoiceSmoke.csproj --configuration Release --no-restore
dotnet build tests/NetCafeManager.BillingSmoke/NetCafeManager.BillingSmoke.csproj --configuration Release --no-restore
```

Các project trên đã build thành công trong bản kiểm tra. BillingSmoke có một cảnh
báo xung đột phiên bản `System.Formats.Asn1` do dependency hiện có, nhưng không có
lỗi biên dịch.

## Commit gợi ý

`fix: enforce prepaid invoice and harden concurrent top-up`
