/*
  Dữ liệu mẫu không chứa tài khoản/mật khẩu.
  Tài khoản quản trị phải được tạo qua luồng bootstrap dùng PBKDF2.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.GOI_GIO WHERE TenGoi = N'Gói 2 giờ')
    BEGIN
        INSERT INTO dbo.GOI_GIO (TenGoi, SoGio, GiaTien, TrangThai)
        VALUES (N'Gói 2 giờ', 2, 20000, 1);
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.GOI_GIO WHERE TenGoi = N'Gói 5 giờ')
    BEGIN
        INSERT INTO dbo.GOI_GIO (TenGoi, SoGio, GiaTien, TrangThai)
        VALUES (N'Gói 5 giờ', 5, 45000, 1);
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.GOI_GIO WHERE TenGoi = N'Gói 10 giờ')
    BEGIN
        INSERT INTO dbo.GOI_GIO (TenGoi, SoGio, GiaTien, TrangThai)
        VALUES (N'Gói 10 giờ', 10, 80000, 1);
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.MAY_TINH WHERE TenMay = N'MAY-01')
    BEGIN
        INSERT INTO dbo.MAY_TINH (TenMay, DiaChiIP, TrangThai)
        VALUES (N'MAY-01', '192.168.1.101', N'Trong');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.MAY_TINH WHERE TenMay = N'MAY-02')
    BEGIN
        INSERT INTO dbo.MAY_TINH (TenMay, DiaChiIP, TrangThai)
        VALUES (N'MAY-02', '192.168.1.102', N'Trong');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.DICH_VU WHERE TenDV = N'Mì gói')
    BEGIN
        INSERT INTO dbo.DICH_VU
            (TenDV, LoaiDV, DonViTinh, DonGia, SoLuongTon, MucTonChuan)
        VALUES
            (N'Mì gói', N'SanPham', N'Gói', 15000, 50, 50);
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.DICH_VU WHERE TenDV = N'Nước suối')
    BEGIN
        INSERT INTO dbo.DICH_VU
            (TenDV, LoaiDV, DonViTinh, DonGia, SoLuongTon, MucTonChuan)
        VALUES
            (N'Nước suối', N'SanPham', N'Chai', 10000, 40, 40);
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.DICH_VU WHERE TenDV = N'Ngủ qua đêm')
    BEGIN
        INSERT INTO dbo.DICH_VU
            (TenDV, LoaiDV, DonViTinh, DonGia, SoLuongTon, MucTonChuan)
        VALUES
            (N'Ngủ qua đêm', N'NguQuaDem', N'Lượt', 50000, NULL, NULL);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
