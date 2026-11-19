# Lập hóa đơn

## Chính sách nghiệp vụ

- Hệ thống dùng mô hình trả trước, không hỗ trợ trạng thái công nợ.
- Mỗi phiên đã kết thúc chỉ có một hóa đơn.
- Tiền giờ lấy từ ảnh chụp `UsageCost` của phiên.
- Tiền dịch vụ lấy từ số lượng và đơn giá đã lưu trong chi tiết dịch vụ.
- Hóa đơn mới có trạng thái `ChuaThanhToan`.
- Quản trị viên chỉ có thể xác nhận hóa đơn sang `DaThanhToan`.

## Không trừ số dư lần hai

`UsageSessionService.EndAsync` đã trừ tiền giờ khi kết thúc phiên. Vì vậy,
`InvoiceService.SetStatusAsync` chỉ xác nhận trạng thái hóa đơn và không trừ lại
`TotalAmount` khỏi tài khoản khách hàng. Gọi xác nhận lại một hóa đơn đã thanh toán
sẽ trả về kết quả thành công hiện có mà không thay đổi số dư.

## Giảm giá

Tạm thời `Discount` phải bằng `0`. Giảm giá chỉ được mở lại khi việc tính giảm giá
được đưa vào cùng transaction đã trừ tiền, để hóa đơn và số dư không lệch nhau.

## Chống tạo trùng

`CreateAsync` chạy trong transaction `Serializable` và cơ sở dữ liệu có ràng buộc
một hóa đơn trên một phiên. Gọi lại cùng mã phiên sẽ trả về hóa đơn hiện có.

## Phần chưa hoàn thiện

Luồng mua sản phẩm phải được triển khai riêng để trừ số dư, trừ tồn kho và ghi nhận
chi tiết dịch vụ trong cùng một transaction. Việc xác nhận hóa đơn không thay thế
transaction mua sản phẩm.
