# Gói 02 - Nạp tiền, số dư và gói giờ

## Nhánh đề xuất

`feature/balance-topup-hour-packages`

## Điều kiện trước khi áp dụng

- Nhánh OTP đã merge vào `main`.
- Nên merge gói quản lý khách hàng trước để luồng Admin đầy đủ hơn.
- Gói code vẫn tự chứa phần kiểm tra quyền cần thiết nên có thể build độc lập.

## Cách áp dụng

Chép `.github`, `docs`, `src`, `tests` vào thư mục chứa `NetCafeManager.sln`.

## Kiểm tra

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet build tests/NetCafeManager.BillingSmoke --configuration Release
```

## Commit đề xuất

`Thêm: Nạp tiền, số dư và gói giờ`

## Phần đang chờ Quân

Tự động trừ tiền theo phiên, gửi cảnh báo qua TCP và khóa máy khi hết tiền.
