# Điều khiển máy trạm từ xa

## Phạm vi đã triển khai

Quản trị viên có hai lệnh qua bảng LENH_DIEU_KHIEN:

- KhoaMay: kết thúc phiên đang sử dụng, đăng xuất tài khoản và khóa máy trạm.
- DangXuat: kết thúc phiên đang sử dụng và đưa giao diện về màn hình đăng nhập.

Chức năng này không tắt máy, khởi động lại máy hoặc chạy lệnh hệ điều hành.

## Luồng xử lý

1. Quản trị viên chọn máy trong tab **Máy trạm**.
2. Server kiểm tra quyền Administrator và khóa hàng đợi của máy bằng
   sp_getapplock.
3. Server chỉ cho phép một lệnh chưa hoàn tất trên mỗi máy.
4. Heartbeat lấy lệnh đang chờ và chuyển trạng thái sang DaGui.
5. Client kết thúc phiên sử dụng, đăng xuất rồi gửi
   MACHINE_REMOTE_COMMAND_RESULT.
6. Server ghi ThanhCong hoặc ThatBai, thời điểm ACK, mã lỗi và lịch sử
   trạng thái máy.

Nếu Client mất kết nối trước khi Server lưu kết quả, heartbeat sau sẽ nhận lại
lệnh chưa hoàn tất. Client dùng CommandId để tránh xử lý đồng thời cùng một
lệnh.

## Dữ liệu và nhật ký

Migration 006_admin_audit_machine_history.sql bổ sung:

- NHAT_KY_QUAN_TRI: nhật ký thao tác quản trị.
- LICH_SU_TRANG_THAI_MAY: lịch sử thay đổi trạng thái máy.

Mọi thao tác đổi trạng thái hoặc xếp lệnh điều khiển đều ghi nhật ký quản trị.

## Kiểm tra

    dotnet build .\NetCafeManager.sln --configuration Release
    dotnet run --project .\tests\NetCafeManager.RemoteMachineControlSmoke --configuration Release

Smoke test SQL cần biến NETCAFE_ADMIN_SMOKE_CONNECTION.
