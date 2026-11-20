/*
  Migration 005: chống xử lý lặp giao dịch mua sản phẩm.
  Chạy sau migration 004_balance_refund.sql.
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

    IF COL_LENGTH('dbo.CHI_TIET_DICH_VU', 'CommandId') IS NULL
    BEGIN
        ALTER TABLE dbo.CHI_TIET_DICH_VU
            ADD CommandId UNIQUEIDENTIFIER NULL;
    END;

    IF COL_LENGTH('dbo.CHI_TIET_DICH_VU', 'SoLuongTonSau') IS NULL
    BEGIN
        ALTER TABLE dbo.CHI_TIET_DICH_VU
            ADD SoLuongTonSau INT NULL;
    END;

    IF COL_LENGTH('dbo.CHI_TIET_DICH_VU', 'SoDuSauGiaoDich') IS NULL
    BEGIN
        ALTER TABLE dbo.CHI_TIET_DICH_VU
            ADD SoDuSauGiaoDich DECIMAL(12, 2) NULL;
    END;

    IF COL_LENGTH('dbo.CHI_TIET_DICH_VU', 'ThoiDiemMua') IS NULL
    BEGIN
        ALTER TABLE dbo.CHI_TIET_DICH_VU
            ADD ThoiDiemMua DATETIME2 NULL;
    END;

    /*
      SQL Server biên dịch cả batch trước khi chạy ALTER TABLE. Dùng dynamic SQL
      để đoạn này chỉ được biên dịch sau khi bốn cột mới đã thực sự tồn tại.
    */
    EXEC sys.sp_executesql N'
        UPDATE dbo.CHI_TIET_DICH_VU
           SET CommandId = NEWID()
         WHERE CommandId IS NULL;

        UPDATE dbo.CHI_TIET_DICH_VU
           SET SoLuongTonSau = 0
         WHERE SoLuongTonSau IS NULL;

        UPDATE dbo.CHI_TIET_DICH_VU
           SET SoDuSauGiaoDich = 0
         WHERE SoDuSauGiaoDich IS NULL;

        UPDATE dbo.CHI_TIET_DICH_VU
           SET ThoiDiemMua = SYSUTCDATETIME()
         WHERE ThoiDiemMua IS NULL;

        ALTER TABLE dbo.CHI_TIET_DICH_VU
            ALTER COLUMN CommandId UNIQUEIDENTIFIER NOT NULL;

        ALTER TABLE dbo.CHI_TIET_DICH_VU
            ALTER COLUMN SoLuongTonSau INT NOT NULL;

        ALTER TABLE dbo.CHI_TIET_DICH_VU
            ALTER COLUMN SoDuSauGiaoDich DECIMAL(12, 2) NOT NULL;

        ALTER TABLE dbo.CHI_TIET_DICH_VU
            ALTER COLUMN ThoiDiemMua DATETIME2 NOT NULL;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.check_constraints
            WHERE name = N''CK_CHI_TIET_DICH_VU_SoLuongTonSau''
              AND parent_object_id = OBJECT_ID(N''dbo.CHI_TIET_DICH_VU'')
        )
        BEGIN
            ALTER TABLE dbo.CHI_TIET_DICH_VU
                ADD CONSTRAINT CK_CHI_TIET_DICH_VU_SoLuongTonSau
                    CHECK (SoLuongTonSau >= 0);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.check_constraints
            WHERE name = N''CK_CHI_TIET_DICH_VU_SoDuSauGiaoDich''
              AND parent_object_id = OBJECT_ID(N''dbo.CHI_TIET_DICH_VU'')
        )
        BEGIN
            ALTER TABLE dbo.CHI_TIET_DICH_VU
                ADD CONSTRAINT CK_CHI_TIET_DICH_VU_SoDuSauGiaoDich
                    CHECK (SoDuSauGiaoDich >= 0);
        END;

        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes
            WHERE name = N''UX_CHI_TIET_DICH_VU_CommandId''
              AND object_id = OBJECT_ID(N''dbo.CHI_TIET_DICH_VU'')
        )
        BEGIN
            CREATE UNIQUE INDEX UX_CHI_TIET_DICH_VU_CommandId
                ON dbo.CHI_TIET_DICH_VU (CommandId);
        END;
    ';

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO

