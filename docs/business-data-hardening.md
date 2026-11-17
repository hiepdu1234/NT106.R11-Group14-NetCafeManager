# Business data hardening

## Thay đổi

- `TopUpService`: kiểm tra `CommandId`, khách hàng, quản trị viên, gói giờ, giá và giới hạn số dư; cập nhật số dư và lịch sử trong transaction `Serializable`; trả lại receipt đã lưu khi nhận lại cùng `CommandId`.
- `ProductInventoryService`: giữ cơ chế transaction `Serializable` và `sp_getapplock` khi điều chỉnh tồn kho; kiểm tra số lượng không âm, tràn số nguyên và giới hạn đơn giá theo `DECIMAL(12,2)`.
- `InvoiceService`: kiểm tra giá trị tiền; khi chuyển hóa đơn sang `DaThanhToan`, trừ số dư khách hàng và cập nhật trạng thái hóa đơn trong cùng transaction. Nếu thiếu tiền hoặc tài khoản không hợp lệ, không cập nhật hóa đơn. Hóa đơn đã thanh toán không thể bị trừ lần nữa.

## Database

Chạy `database/migrations/002_business_data_hardening.sql` sau `001_initial_schema.sql` trên database hiện có. Migration thêm `CommandId`, `SoDuSauGiaoDich` và unique index cho `CommandId`. Các bản ghi nạp tiền cũ được gán CommandId mới; snapshot số dư của bản ghi cũ đặt 0 vì schema cũ không lưu số dư tại thời điểm giao dịch.

## Bên gọi TopUpService

Bên gọi phải tạo một `Guid` mới cho mỗi thao tác nạp tiền, gắn vào `TopUpRequest.CommandId`, và tái sử dụng đúng Guid đó khi gửi lại do timeout/mất kết nối. Không tạo Guid mới cho mỗi lần retry.

## Giới hạn còn lại

- Chưa có luồng đặt hàng/bán sản phẩm trong Server; do đó chưa có nơi phù hợp để trừ tồn kho khi bán. `AdjustStockAsync` chỉ là điều chỉnh kho thủ công, không thay thế quy trình bán hàng.
- Chưa có luồng thanh toán qua TCP cho hóa đơn; hiện thay đổi được thực hiện qua `InvoiceService`.
- Smoke tests cần chạy với SQL Server có áp dụng cả migration 001 và 002.
