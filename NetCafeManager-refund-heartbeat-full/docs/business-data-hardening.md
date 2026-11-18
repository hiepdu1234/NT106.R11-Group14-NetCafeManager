# Business data hardening

## Thay đổi

- `TopUpService` dùng `CommandId` để chống xử lý lại cùng yêu cầu, lưu
  `BalanceAfter`, và dùng `sp_getapplock` theo lệnh cùng tài khoản để khóa các lần
  nạp đồng thời.
- `ProductInventoryService` giữ transaction `Serializable` và `sp_getapplock` khi
  điều chỉnh tồn kho; kiểm tra số lượng, đơn giá và giới hạn dữ liệu.
- `InvoiceService` áp dụng chính sách trả trước: bỏ `CongNo`, không trừ số dư lần
  hai khi xác nhận hóa đơn, và xử lý việc xác nhận lặp lại theo hướng idempotent.
- Giảm giá khác `0` tạm thời bị từ chối cho đến khi được tích hợp vào transaction
  tính tiền.

## Database

Đối với cơ sở dữ liệu mới, chạy theo thứ tự:

1. `database/migrations/001_initial_schema.sql`
2. `database/migrations/002_business_data_hardening.sql`
3. `database/migrations/003_prepaid_invoice_policy.sql`
4. `database/seed/001_seed_core.sql`

Migration 002 thêm `CommandId`, `SoDuSauGiaoDich` và unique index cho `CommandId`.
Các bản ghi cũ được cấp `CommandId` mới; `SoDuSauGiaoDich` của dữ liệu cũ được đặt
bằng `0` vì schema trước đó không lưu snapshot số dư.

Migration 003 chuyển dữ liệu `CongNo` cũ về `ChuaThanhToan`, sau đó thay ràng buộc
trạng thái để chỉ còn `ChuaThanhToan` và `DaThanhToan`.

## Quy ước gọi TopUpService

Bên gọi tạo một `Guid` mới cho mỗi thao tác nạp tiền và gắn vào
`TopUpRequest.CommandId`. Khi retry do timeout hoặc mất kết nối, phải dùng lại đúng
`Guid` đó và giữ nguyên khách hàng, gói giờ, quản trị viên cùng ghi chú. Không tạo
`Guid` mới cho mỗi lần retry.

## Giới hạn còn lại

- Chưa có transaction mua sản phẩm gồm trừ số dư, trừ tồn kho, ghi chi tiết dịch
  vụ và lập hóa đơn. Phần này phải được làm ở nhánh riêng
  `feature/product-purchase-transaction`.
- Chưa có luồng thanh toán hóa đơn qua TCP; hiện mới có service nghiệp vụ.
- `BalanceAfter = 0` ở các bản ghi nạp cũ chỉ là giá trị chuyển đổi schema, không
  phải số dư lịch sử đã được xác minh.
