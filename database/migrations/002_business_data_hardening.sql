/*
  NetCafeManager - Migration 002: Business data hardening.
  Chay sau 001_initial_schema.sql tren database da co schema.
  Them khoa idempotency va so du sau moi giao dich nap de co the tra lai ket qua cu.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    ALTER TABLE dbo.GIAO_DICH_NAP
        ADD CommandId UNIQUEIDENTIFIER NULL,
            SoDuSauGiaoDich DECIMAL(12, 2) NULL;

    /* Du lieu cu khong co ma lenh; cap ma moi va gia tri snapshot an toan. */
    UPDATE dbo.GIAO_DICH_NAP
       SET CommandId = NEWID(),
           SoDuSauGiaoDich = 0
     WHERE CommandId IS NULL OR SoDuSauGiaoDich IS NULL;

    ALTER TABLE dbo.GIAO_DICH_NAP
        ALTER COLUMN CommandId UNIQUEIDENTIFIER NOT NULL;
    ALTER TABLE dbo.GIAO_DICH_NAP
        ALTER COLUMN SoDuSauGiaoDich DECIMAL(12, 2) NOT NULL;

    ALTER TABLE dbo.GIAO_DICH_NAP
        ADD CONSTRAINT CK_GIAO_DICH_NAP_SoDuSau
            CHECK (SoDuSauGiaoDich >= 0);

    CREATE UNIQUE INDEX UX_GIAO_DICH_NAP_CommandId
        ON dbo.GIAO_DICH_NAP(CommandId);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
