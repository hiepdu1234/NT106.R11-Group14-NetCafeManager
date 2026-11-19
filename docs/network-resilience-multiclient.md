# Kiem thu timeout va nhieu Client

## Pham vi

Phan timeout 15 giay da co tu PR #26. `TcpServer` theo doi heartbeat cuoi cua
tung ket noi; khi qua thoi gian quy dinh, Server dong rieng socket do. Luong don
dep hien co tiep tuc ket thuc phien, dang xuat va cap nhat may thanh
`MatKetNoi`.

Nhanh nay bo sung kiem thu socket that, khong tao lai nghiep vu heartbeat.

## Cac truong hop duoc kiem tra

1. Hai muoi bon Client ket noi dong thoi va gui tong cong 96 frame.
2. Mot Client tat dot ngot bang TCP reset; Server van phuc vu Client tiep theo.
3. Client gui length-prefix vuot gioi han 64 KiB; chi ket noi loi bi dong.
4. Client gui payload khong phai JSON; Server van tiep tuc lang nghe.
5. Server dung roi khoi dong lai tren cung cong va nhan request moi.
6. Ket noi da co heartbeat bi dong khi vuot timeout.

## Chay kiem thu

```powershell
dotnet run --project tests/NetCafeManager.NetworkResilienceSmoke --configuration Release
```

Smoke test nay khong can SQL Server. GitHub Actions chay tren ca Windows va
Ubuntu de kiem tra hanh vi socket tren hai he dieu hanh.

## Tieu chi dat

- Khong co request nao cua nhom Client dong thoi bi that lac.
- Response giu dung `replyTo` tuong ung voi request.
- Frame sai khong lam dung toan bo Server.
- Client moi van ket noi duoc sau TCP reset va sau frame sai.
- Server bind lai duoc cung cong sau khi restart.
- Ket noi heartbeat qua han bi dong va context duoc danh dau timeout.
