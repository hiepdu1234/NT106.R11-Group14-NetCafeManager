/*
  Migration 002: bổ sung dữ liệu an toàn cho giao dịch nạp tiền.
  Chạy sau migration 001_initial_schema.sql.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

------------------------------------------------------------
-- PHẦN 1: TẠO HAI CỘT MỚI
------------------------------------------------------------

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH('dbo.GIAO_DICH_NAP', 'CommandId') IS NULL
    BEGIN
        ALTER TABLE dbo.GIAO_DICH_NAP
            ADD CommandId UNIQUEIDENTIFIER NULL;
    END;

    IF COL_LENGTH('dbo.GIAO_DICH_NAP', 'SoDuSauGiaoDich') IS NULL
    BEGIN
        ALTER TABLE dbo.GIAO_DICH_NAP
            ADD SoDuSauGiaoDich DECIMAL(12, 2) NULL;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO

------------------------------------------------------------
-- PHẦN 2: CẬP NHẬT VÀ RÀNG BUỘC DỮ LIỆU
------------------------------------------------------------

BEGIN TRY
    BEGIN TRANSACTION;

    -- Bổ sung giá trị cho các giao dịch cũ.
    UPDATE dbo.GIAO_DICH_NAP
       SET CommandId = NEWID()
     WHERE CommandId IS NULL;

    UPDATE dbo.GIAO_DICH_NAP
       SET SoDuSauGiaoDich = 0
     WHERE SoDuSauGiaoDich IS NULL;

    -- Sau khi đã có dữ liệu, không cho phép hai cột bị NULL.
    ALTER TABLE dbo.GIAO_DICH_NAP
        ALTER COLUMN CommandId UNIQUEIDENTIFIER NOT NULL;

    ALTER TABLE dbo.GIAO_DICH_NAP
        ALTER COLUMN SoDuSauGiaoDich DECIMAL(12, 2) NOT NULL;

    -- Số dư sau giao dịch không được âm.
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = N'CK_GIAO_DICH_NAP_SoDuSau'
          AND parent_object_id = OBJECT_ID(N'dbo.GIAO_DICH_NAP')
    )
    BEGIN
        ALTER TABLE dbo.GIAO_DICH_NAP
            ADD CONSTRAINT CK_GIAO_DICH_NAP_SoDuSau
                CHECK (SoDuSauGiaoDich >= 0);
    END;

    -- Mỗi CommandId chỉ được sử dụng cho một giao dịch.
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes
        WHERE name = N'UX_GIAO_DICH_NAP_CommandId'
          AND object_id = OBJECT_ID(N'dbo.GIAO_DICH_NAP')
    )
    BEGIN
        CREATE UNIQUE INDEX UX_GIAO_DICH_NAP_CommandId
            ON dbo.GIAO_DICH_NAP(CommandId);
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
GO
