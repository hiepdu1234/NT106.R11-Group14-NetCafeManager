/*
  NetCafeManager - Migration 003
  Áp dụng chính sách trả trước, loại bỏ trạng thái công nợ.
  Chạy sau migration 001 và 002.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    UPDATE dbo.HOA_DON
       SET TrangThai = N'ChuaThanhToan'
     WHERE TrangThai = N'CongNo';

    IF EXISTS
    (
        SELECT 1
          FROM sys.check_constraints
         WHERE name = N'CK_HOA_DON_TrangThai'
           AND parent_object_id = OBJECT_ID(N'dbo.HOA_DON')
    )
    BEGIN
        ALTER TABLE dbo.HOA_DON
            DROP CONSTRAINT CK_HOA_DON_TrangThai;
    END;

    ALTER TABLE dbo.HOA_DON
        ADD CONSTRAINT CK_HOA_DON_TrangThai
            CHECK (TrangThai IN (N'ChuaThanhToan', N'DaThanhToan'));

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
