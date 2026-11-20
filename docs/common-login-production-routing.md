# Đăng nhập chung và định tuyến giao diện

## Mục tiêu

Ứng dụng desktop dùng một màn hình đăng nhập chung. Người dùng không tự chọn
vai trò khi đăng nhập. `NetCafeManager.Server` xác thực tài khoản, đọc vai trò
từ SQL Server và trả kết quả để ứng dụng mở giao diện khách hàng hoặc quản trị.

## Phân chia trách nhiệm

| Thành phần | Trách nhiệm |
| --- | --- |
| Desktop | Nhập cấu hình, xác thực thiết bị, nhập thông tin đăng nhập và hiển thị giao diện |
| Server | Xác thực máy, mật khẩu, vai trò, phiên và toàn bộ quyền nghiệp vụ |
| SQL Server | Lưu dữ liệu lâu dài; chỉ Server được kết nối trực tiếp |

Chế độ thiết bị (`Máy khách` hoặc `Máy quản trị`) là cấu hình cục bộ để hỗ trợ
vận hành. Nó không cấp quyền. Quyền thực tế luôn lấy từ `LoginResponse.User.Role`.

## Thiết lập lần đầu

Người cài đặt cấu hình:

1. Loại thiết bị.
2. Địa chỉ và cổng Server.
3. Tên máy đã được đăng ký trên Server.
4. Khóa máy Base64 do quản trị viên cấp.

Cấu hình được lưu tại thư mục ứng dụng của người dùng Windows. Khóa máy được
mã hóa bằng Windows DPAPI với phạm vi `CurrentUser`, không lưu dạng văn bản thuần.

## Luồng đăng nhập

1. Desktop thực hiện challenge-response để xác thực thiết bị.
2. Người dùng nhập tên đăng nhập và mật khẩu trên màn hình chung.
3. Server kiểm tra mật khẩu PBKDF2, trạng thái và vai trò tài khoản.
4. Desktop đối chiếu vai trò với loại thiết bị đã cấu hình.
5. Tài khoản `Client` mở phiên sử dụng và chuyển đến giao diện khách hàng.
6. Tài khoản `Administrator` chuyển đến giao diện quản trị; không mở phiên tính giờ.

## Luồng đăng xuất

- Client: Server kết thúc phiên sử dụng, tính chi phí, cập nhật database rồi thu
  hồi token đăng nhập.
- Administrator: Server thu hồi token đăng nhập; không tạo hoặc tính phí phiên sử dụng.
- Sau khi Server xác nhận, Desktop quay lại màn hình đăng nhập chung.

## Chức năng đã nối sau gói nền tảng

Giao diện quản trị hiện đã có quản lý tài khoản, gói giờ/nạp tiền, hoàn tiền,
hóa đơn và kho/sản phẩm qua TCP. Heartbeat cũng trả cảnh báo số dư và Server tự
kết thúc phiên khi hết tiền. Chế độ LAN dùng TLS và xác minh SHA-256 chứng thư
được hướng dẫn tại [admin-operations-tcp-ui.md](admin-operations-tcp-ui.md).
