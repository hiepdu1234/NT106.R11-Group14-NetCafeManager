# Cách áp dụng bằng GitHub Desktop

Gói này được tạo từ `main` tại commit `1cd106b` và đã tích hợp phần hoàn tiền có
kiểm soát cùng heartbeat vào đúng project gốc. Các thay đổi xác thực của PR #24
và mua sản phẩm của PR #25 vẫn được giữ nguyên.

## Cách áp dụng

1. Trong GitHub Desktop, chọn repository `NT106.R11-Group14-NetCafeManager`.
2. Fetch và pull `main` mới nhất.
3. Tạo branch mới: `fix/refund-heartbeat-root-integration`.
4. Chọn **Repository > Show in Explorer**.
5. Xóa thư mục sai `NetCafeManager-refund-heartbeat-full` đang nằm trong repository.
6. Sao chép toàn bộ nội dung đã giải nén của gói này vào thư mục gốc repository và
   chọn thay thế các tệp trùng tên.
7. Quay lại GitHub Desktop, kiểm tra thay đổi rồi tự commit và push.

## Nội dung chính

- Thêm migration `004_balance_refund.sql`.
- Hoàn toàn bộ số dư do nhân viên/quản trị thực hiện, có `CommandId` chống gửi
  trùng và khóa tài khoản sau khi hoàn tiền.
- Ghi lịch sử hoàn tiền và bảo vệ transaction khi có yêu cầu đồng thời.
- Client gửi heartbeat định kỳ sau khi xác thực máy.
- Server trả ACK, theo dõi heartbeat cuối và ngắt kết nối khi timeout.
- Kết thúc phiên sử dụng, đăng xuất và cập nhật trạng thái máy khi mất kết nối.
- Giữ nguyên phần xác thực của PR #24 và Product Purchase của PR #25.
- Thêm smoke test và GitHub Actions cho refund/heartbeat.

## Kiểm tra trước khi push

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.RefundSmoke --configuration Release
dotnet build tests/NetCafeManager.NetworkIntegrationSmoke --configuration Release
```

Hai smoke test chạy đầy đủ khi có SQL Server và connection string tương ứng.
GitHub Actions sẽ tự tạo SQL Server container, chạy migration rồi thực thi test.

## Commit gợi ý

`fix: integrate controlled refund and heartbeat into root project`
