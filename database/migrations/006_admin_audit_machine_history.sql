/*
  Migration 006: administrator audit log and machine-status history.
  Run after migration 005_product_purchase_idempotency.sql.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.NHAT_KY_QUAN_TRI', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.NHAT_KY_QUAN_TRI
        (
            MaNhatKy BIGINT IDENTITY(1, 1) NOT NULL,
            MaAdmin UNIQUEIDENTIFIER NOT NULL,
            HanhDong VARCHAR(80) NOT NULL,
            LoaiDoiTuong VARCHAR(50) NOT NULL,
            MaDoiTuong NVARCHAR(100) NULL,
            ThanhCong BIT NOT NULL,
            MoTa NVARCHAR(500) NOT NULL,
            ThoiDiem DATETIME2 NOT NULL,
            CONSTRAINT PK_NHAT_KY_QUAN_TRI PRIMARY KEY (MaNhatKy),
            CONSTRAINT FK_NHAT_KY_QUAN_TRI_TAI_KHOAN FOREIGN KEY (MaAdmin)
                REFERENCES dbo.TAI_KHOAN (MaTK)
        );

        CREATE INDEX IX_NHAT_KY_QUAN_TRI_ThoiDiem
            ON dbo.NHAT_KY_QUAN_TRI (ThoiDiem DESC);
        CREATE INDEX IX_NHAT_KY_QUAN_TRI_MaAdmin
            ON dbo.NHAT_KY_QUAN_TRI (MaAdmin);
    END;

    IF OBJECT_ID(N'dbo.LICH_SU_TRANG_THAI_MAY', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.LICH_SU_TRANG_THAI_MAY
        (
            MaLichSu BIGINT IDENTITY(1, 1) NOT NULL,
            MaMay INT NOT NULL,
            MaAdmin UNIQUEIDENTIFIER NOT NULL,
            TrangThaiCu NVARCHAR(20) NOT NULL,
            TrangThaiMoi NVARCHAR(20) NOT NULL,
            LyDo NVARCHAR(500) NULL,
            ThoiDiem DATETIME2 NOT NULL,
            CONSTRAINT PK_LICH_SU_TRANG_THAI_MAY PRIMARY KEY (MaLichSu),
            CONSTRAINT FK_LICH_SU_TRANG_THAI_MAY_MAY_TINH FOREIGN KEY (MaMay)
                REFERENCES dbo.MAY_TINH (MaMay),
            CONSTRAINT FK_LICH_SU_TRANG_THAI_MAY_TAI_KHOAN FOREIGN KEY (MaAdmin)
                REFERENCES dbo.TAI_KHOAN (MaTK),
            CONSTRAINT CK_LICH_SU_TRANG_THAI_MAY_TrangThaiCu CHECK
            (
                TrangThaiCu IN
                (
                    N'Trong', N'DangSuDung', N'Khoa', N'BaoTri',
                    N'SuCo', N'MatKetNoi'
                )
            ),
            CONSTRAINT CK_LICH_SU_TRANG_THAI_MAY_TrangThaiMoi CHECK
            (
                TrangThaiMoi IN
                (
                    N'Trong', N'DangSuDung', N'Khoa', N'BaoTri',
                    N'SuCo', N'MatKetNoi'
                )
            )
        );

        CREATE INDEX IX_LICH_SU_TRANG_THAI_MAY_MaMay_ThoiDiem
            ON dbo.LICH_SU_TRANG_THAI_MAY (MaMay, ThoiDiem DESC);
        CREATE INDEX IX_LICH_SU_TRANG_THAI_MAY_MaAdmin
            ON dbo.LICH_SU_TRANG_THAI_MAY (MaAdmin);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
