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

## Giới hạn của gói nền tảng này

Gói này thiết lập cấu hình thiết bị, đăng nhập chung và định tuyến hai giao diện.
Các màn hình nghiệp vụ quản lý tài khoản, máy trạm, hóa đơn, số dư và tồn kho sẽ
được nối vào giao diện quản trị ở các gói tiếp theo. Trước khi mở Server ra mạng
LAN thật, kết nối TCP phải được bảo vệ bằng TLS và chứng thư Server phải được
xác minh tại Desktop.
