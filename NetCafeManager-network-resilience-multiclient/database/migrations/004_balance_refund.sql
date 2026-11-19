/*
  NetCafeManager - Migration 004
  Luu vet hoan toan bo so du khi quan tri vien dong tai khoan khach hang.
  Chay sau migration 001, 002 va 003.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.GIAO_DICH_HOAN_TIEN', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.GIAO_DICH_HOAN_TIEN
        (
            MaHoan BIGINT IDENTITY(1, 1) NOT NULL,
            CommandId UNIQUEIDENTIFIER NOT NULL,
            MaTK UNIQUEIDENTIFIER NOT NULL,
            SoTien DECIMAL(12, 2) NOT NULL,
            SoDuTruocGiaoDich DECIMAL(12, 2) NOT NULL,
            SoDuSauGiaoDich DECIMAL(12, 2) NOT NULL,
            NguoiThucHien UNIQUEIDENTIFIER NOT NULL,
            PhuongThuc NVARCHAR(20) NOT NULL,
            LyDo NVARCHAR(500) NOT NULL,
            ThoiDiem DATETIME2 NOT NULL,

            CONSTRAINT PK_GIAO_DICH_HOAN_TIEN PRIMARY KEY (MaHoan),
            CONSTRAINT FK_GIAO_DICH_HOAN_TIEN_TAI_KHOAN
                FOREIGN KEY (MaTK) REFERENCES dbo.TAI_KHOAN (MaTK),
            CONSTRAINT FK_GIAO_DICH_HOAN_TIEN_NGUOI_THUC_HIEN
                FOREIGN KEY (NguoiThucHien) REFERENCES dbo.TAI_KHOAN (MaTK),
            CONSTRAINT CK_GIAO_DICH_HOAN_TIEN_SoTien
                CHECK (SoTien > 0),
            CONSTRAINT CK_GIAO_DICH_HOAN_TIEN_SoDuTruoc
                CHECK (SoDuTruocGiaoDich > 0),
            CONSTRAINT CK_GIAO_DICH_HOAN_TIEN_SoDuSau
                CHECK (SoDuSauGiaoDich = 0),
            CONSTRAINT CK_GIAO_DICH_HOAN_TIEN_SoTienKhopSoDu
                CHECK (SoTien = SoDuTruocGiaoDich - SoDuSauGiaoDich),
            CONSTRAINT CK_GIAO_DICH_HOAN_TIEN_PhuongThuc
                CHECK (PhuongThuc IN (N'TienMat', N'ChuyenKhoan')),
            CONSTRAINT CK_GIAO_DICH_HOAN_TIEN_LyDo
                CHECK (LEN(LTRIM(RTRIM(LyDo))) > 0)
        );

        CREATE UNIQUE INDEX UX_GIAO_DICH_HOAN_TIEN_CommandId
            ON dbo.GIAO_DICH_HOAN_TIEN (CommandId);
        CREATE INDEX IX_GIAO_DICH_HOAN_TIEN_MaTK
            ON dbo.GIAO_DICH_HOAN_TIEN (MaTK);
        CREATE INDEX IX_GIAO_DICH_HOAN_TIEN_NguoiThucHien
            ON dbo.GIAO_DICH_HOAN_TIEN (NguoiThucHien);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
