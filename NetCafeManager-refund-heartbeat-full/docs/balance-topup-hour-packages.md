# Số dư, nạp tiền và gói giờ

## Phạm vi

- Quản trị viên tạo hoặc cập nhật gói giờ.
- Khách chọn gói theo số giờ; quản trị viên xác nhận nạp thủ công.
- Giá của gói được cộng vào số dư tiền của tài khoản.
- Giao dịch lưu ảnh chụp số tiền, số giờ, người thực hiện và thời điểm.
- Khách chỉ xem lịch sử của chính mình; quản trị viên xem được lịch sử khách hàng.

## Tính nhất quán

`TopUpService` cập nhật số dư và tạo `GIAO_DICH_NAP` trong cùng một transaction
`Serializable`. Nếu một bước thất bại thì toàn bộ giao dịch được rollback, tránh
cộng số dư nhưng thiếu lịch sử hoặc tạo lịch sử mà chưa cộng số dư.

## Cảnh báo sắp hết tiền

`BalanceWarningEvaluator` ước lượng thời gian còn lại từ số dư và đơn giá giờ.
Ngưỡng demo mặc định là 15 phút. Việc gửi cảnh báo tới Client phải chờ tầng TCP
và phiên sử dụng của Quân; gói này chỉ cung cấp logic tính toán độc lập.

## Chưa tích hợp

- Chưa tự động trừ tiền theo phiên.
- Chưa khóa hoặc đăng xuất máy khi số dư bằng 0.
- Chưa gửi thông điệp cảnh báo qua TCP.

Ba phần này phụ thuộc module phiên sử dụng và Client Agent.
