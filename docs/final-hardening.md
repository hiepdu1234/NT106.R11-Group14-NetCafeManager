# Hoan thien giao dich mua hang va reconnect

## 1. Chong gui trung khi mua san pham

- `PurchaseProductRequest` bat buoc co `CommandId`.
- Server khoa theo `CommandId`, tai khoan va san pham trong transaction
  `Serializable`.
- Hai request dong thoi cung ma chi tao mot dong `CHI_TIET_DICH_VU`.
- Retry dung noi dung tra lai bien lai cu; tai su dung ma cho noi dung khac bi
  tu choi bang `PURCHASE_COMMAND_CONFLICT`.
- Migration 005 them unique index va snapshot so du/ton kho sau giao dich.
- Client giu `CommandId` dang cho neu response bi mat, de lan thu lai khong bi
  tinh tien hai lan.

## 2. Kiem thu ket noi lai cua Client

- Dung `TcpClientAgent` that ket noi toi TCP loopback.
- Xac nhan Client gui/nhan frame truoc khi Server dung.
- Xac nhan socket cu that bai sau khi Server dung.
- Client retry khi Server chua san sang, sau do ket noi lai tren cung cong va
  gui request thanh cong khi Server khoi dong lai.

## 3. Lenh kiem tra

```powershell
dotnet build NetCafeManager.sln --configuration Release -p:BuildInParallel=false
dotnet build tests/NetCafeManager.ProductPurchaseSmoke --configuration Release
dotnet build tests/NetCafeManager.ClientReconnectSmoke --configuration Release
dotnet run --project tests/NetCafeManager.ClientReconnectSmoke --configuration Release
```

Smoke test mua hang can SQL Server va bien moi truong
`NETCAFE_PURCHASE_SMOKE_CONNECTION`. GitHub Actions tu tao SQL Server, chay tat
ca migration theo thu tu va thuc thi test.

