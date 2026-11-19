# Cach ap dung goi Network Resilience

Goi nay duoc tao tu `main` sau khi PR #26 da merge.

## Thao tac bang GitHub Desktop

1. Chuyen ve nhanh `main`, bam `Fetch origin`, sau do `Pull origin` neu co.
2. Tao nhanh moi tu `main`: `test/network-resilience-multiclient`.
3. Giai nen goi ZIP.
4. Sao chep toan bo noi dung trong thu muc da giai nen vao repository hien tai
   va cho phep ghi de cac tep trung ten.
5. Mo GitHub Desktop va kiem tra danh sach thay doi.
6. Commit voi summary goi y:
   `test: add TCP timeout and multi-client resilience smoke`
7. Publish branch, tao pull request vao `main`, va doi GitHub Actions chay xong.

## Kiem tra tai may

```powershell
dotnet build NetCafeManager.sln --configuration Release
dotnet run --project tests/NetCafeManager.NetworkResilienceSmoke --configuration Release
```

Dong cuoi cua smoke test phai co `Network resilience smoke passed`.
