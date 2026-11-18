# Hoan tien co kiem soat va heartbeat

## Pham vi da trien khai

- Hoan toan bo so du khi quan tri vien dong tai khoan khach hang.
- Khong cho hoan neu tai khoan con phien su dung dang hoat dong.
- Ghi rieng giao dich `GIAO_DICH_HOAN_TIEN` va cap nhat so du ve 0, khoa tai khoan trong cung transaction.
- Dung `CommandId` duy nhat de gui lai an toan, khong hoan hai lan.
- Luu so du truoc/sau, so tien, quan tri vien, phuong thuc, ly do va thoi diem.
- Client gui heartbeat moi 5 giay sau khi handshake thanh cong.
- Server ACK, cap nhat `LanHeartbeatCuoi`, va xem ket noi la timeout khi qua 15 giay.
- Khi ket noi bi mat, Server ket thuc phien dang hoat dong truoc khi danh dau may `MatKetNoi`.

## Chinh sach hoan tien

Ban dau chi ho tro hoan toan bo so du khi dong tai khoan, khong ho tro rut mot phan. Phuong thuc hop le la `TienMat` hoac `ChuyenKhoan`. Ly do hoan tien la bat buoc va toi da 500 ky tu.

Thu tu xu ly:

1. Xac minh nguoi thuc hien la quan tri vien dang hoat dong.
2. Khoa `CommandId` va tai khoan khach hang trong transaction `Serializable`.
3. Kiem tra tai khoan khach hang, phien dang hoat dong va so du.
4. Them lich su hoan tien.
5. Dat so du ve 0 va `TrangThai = 0`.
6. Commit mot lan.

## Heartbeat

`TcpClientAgent` bat dau vong heartbeat sau khi `MACHINE_HANDSHAKE_COMPLETE` thanh cong. Heartbeat dung cung ket noi TCP va cung co che length-prefix JSON hien tai.

- Request: `MACHINE_HEARTBEAT`.
- Chu ky Client: 5 giay.
- Timeout Server: lon hon 15 giay khong nhan heartbeat.
- Trang thai sau timeout/ngat ket noi: `MatKetNoi`.

Day la backend va ket noi TCP da hoat dong. Man hinh quan tri de bam hoan tien van la phan UI can lam tiep; khong nen mo Server ra LAN truoc khi bo sung TLS.

## Migration va kiem thu

Chay cac migration theo thu tu ten file, trong do migration moi la:

`database/migrations/004_balance_refund.sql`

Workflow `check-refund-heartbeat.yml` build tren Windows, tao SQL Server tam thoi, chay toan bo migration, sau do chay refund smoke va network integration smoke.
