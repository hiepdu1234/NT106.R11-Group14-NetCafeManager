# Cach ap dung goi hoan thien cuoi bang GitHub Desktop

Goi nay duoc tao tu `main` sau PR #30, da giu nguyen phan heartbeat, timeout,
kiem thu nhieu Client va cau truc repository da sua dung.

## Nhanh de nghi

`fix/final-purchase-idempotency-client-reconnect`

## Noi dung

- Hoan thien phan Quan: chong gui trung giao dich mua san pham bang `CommandId`.
- Them migration `005_product_purchase_idempotency.sql` va unique index.
- Luu snapshot so du, ton kho va thoi diem mua de retry tra dung ket qua cu.
- Client giu lai `CommandId` dang cho neu response bi mat, de khong tinh tien hai
  lan sau khi reconnect.
- Them smoke test Client mat ket noi, retry va phuc hoi sau khi Server restart.

## Cach ap dung

1. Fetch/Pull `main` moi nhat trong GitHub Desktop.
2. Tao branch `fix/final-purchase-idempotency-client-reconnect` tu `main`.
3. Giai nen goi vao thu muc chua `NetCafeManager.sln` va ghi de file trung.
4. Build va chay cac lenh trong `docs/final-hardening.md`.
5. Commit, publish branch va tao Pull Request vao `main`.

## Commit goi y

`fix: finalize purchase idempotency and client reconnect tests`
