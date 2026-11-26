# Cách áp dụng gói remote-machine-control delta

Gói này được tạo từ main hiện hành và chỉ chứa các file thuộc chức năng
quản trị/điều khiển máy trạm.

1. Trong GitHub Desktop, giữ nhánh feature/remote-machine-control.
2. Bảo đảm màn hình hiển thị No local changes.
3. Giải nén gói ZIP.
4. Chép toàn bộ thư mục con trong gói vào thư mục gốc repository và cho phép
   ghi đè các file trùng tên.
5. Không chép thư mục bao ngoài của gói vào repository.
6. Kiểm tra danh sách thay đổi, build solution rồi mới commit.

Không dùng lại gói full cũ vì gói đó làm phát sinh thay đổi LF/CRLF trên nhiều
file không liên quan.
