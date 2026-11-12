# Lập hóa đơn

## Phạm vi

- Lập một hóa đơn duy nhất cho mỗi phiên đã kết thúc.
- Tiền giờ lấy từ ảnh chụp `UsageCost` của phiên.
- Tiền dịch vụ được tính từ số lượng và đơn giá đã lưu trong chi tiết dịch vụ.
- Giảm giá không được âm và không vượt quá tổng tiền trước giảm.
- Hóa đơn mới có trạng thái `ChuaThanhToan`.
- Quản trị viên có thể chuyển sang `DaThanhToan` hoặc `CongNo`.

## Chống tạo trùng

`InvoiceService` chạy trong transaction `Serializable` và kiểm tra ràng buộc một
hóa đơn trên một phiên. Gọi lại cùng mã phiên sẽ trả về hóa đơn hiện có.

## Phụ thuộc chưa nối

Code nghiệp vụ đã hoàn chỉnh nhưng thao tác tự động lập hóa đơn phải chờ module
phiên sử dụng của Quân phát sự kiện kết thúc phiên. Việc trừ số dư khi thanh toán
cũng phải tích hợp với module số dư trong một transaction riêng ở bước tích hợp.
