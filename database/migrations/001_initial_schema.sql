/*
  NetCafeManager - Migration 001
  SQL Server 2019+.
  Chạy trên cơ sở dữ liệu trống đã được chọn trước khi chạy seed.
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

    CREATE TABLE dbo.TAI_KHOAN
    (
        MaTK UNIQUEIDENTIFIER NOT NULL,
        TenDangNhap NVARCHAR(50) NOT NULL,
        PasswordAlgorithm NVARCHAR(40) NOT NULL,
        IterationCount INT NOT NULL
            CONSTRAINT DF_TAI_KHOAN_IterationCount DEFAULT (100000),
        PasswordSaltBase64 VARCHAR(44) NOT NULL,
        PasswordHashBase64 VARCHAR(44) NOT NULL,
        Email NVARCHAR(255) NOT NULL,
        HoTen NVARCHAR(100) NULL,
        VaiTro NVARCHAR(20) NOT NULL,
        SoDu DECIMAL(12, 2) NOT NULL
            CONSTRAINT DF_TAI_KHOAN_SoDu DEFAULT (0),
        TrangThai BIT NOT NULL
            CONSTRAINT DF_TAI_KHOAN_TrangThai DEFAULT (1),
        CONSTRAINT PK_TAI_KHOAN PRIMARY KEY (MaTK),
        CONSTRAINT UQ_TAI_KHOAN_TenDangNhap UNIQUE (TenDangNhap),
        CONSTRAINT UQ_TAI_KHOAN_Email UNIQUE (Email),
        CONSTRAINT CK_TAI_KHOAN_VaiTro
            CHECK (VaiTro IN (N'Administrator', N'Client')),
        CONSTRAINT CK_TAI_KHOAN_SoDu CHECK (SoDu >= 0),
        CONSTRAINT CK_TAI_KHOAN_IterationCount
            CHECK (IterationCount BETWEEN 1 AND 10000000)
    );

    CREATE TABLE dbo.MAY_TINH
    (
        MaMay INT IDENTITY(1, 1) NOT NULL,
        TenMay NVARCHAR(50) NOT NULL,
        DiaChiIP VARCHAR(45) NULL,
        DiaChiMAC VARCHAR(17) NULL,
        TrangThai NVARCHAR(20) NOT NULL
            CONSTRAINT DF_MAY_TINH_TrangThai DEFAULT (N'Trong'),
        LanHeartbeatCuoi DATETIME2 NULL,
        LyDoBaoTri NVARCHAR(500) NULL,
        CONSTRAINT PK_MAY_TINH PRIMARY KEY (MaMay),
        CONSTRAINT UQ_MAY_TINH_TenMay UNIQUE (TenMay),
        CONSTRAINT CK_MAY_TINH_TrangThai CHECK
        (
            TrangThai IN
            (
                N'Trong',
                N'DangSuDung',
                N'Khoa',
                N'BaoTri',
                N'SuCo',
                N'MatKetNoi'
            )
        )
    );

    CREATE UNIQUE INDEX UX_MAY_TINH_DiaChiIP
        ON dbo.MAY_TINH (DiaChiIP)
        WHERE DiaChiIP IS NOT NULL;

    CREATE UNIQUE INDEX UX_MAY_TINH_DiaChiMAC
        ON dbo.MAY_TINH (DiaChiMAC)
        WHERE DiaChiMAC IS NOT NULL;

    CREATE TABLE dbo.GOI_GIO
    (
        MaGoi INT IDENTITY(1, 1) NOT NULL,
        TenGoi NVARCHAR(100) NOT NULL,
        SoGio INT NOT NULL,
        GiaTien DECIMAL(12, 2) NOT NULL,
        TrangThai BIT NOT NULL
            CONSTRAINT DF_GOI_GIO_TrangThai DEFAULT (1),
        CONSTRAINT PK_GOI_GIO PRIMARY KEY (MaGoi),
        CONSTRAINT UQ_GOI_GIO_TenGoi UNIQUE (TenGoi),
        CONSTRAINT CK_GOI_GIO_SoGio CHECK (SoGio > 0),
        CONSTRAINT CK_GOI_GIO_GiaTien CHECK (GiaTien >= 0)
    );

    CREATE TABLE dbo.DICH_VU
    (
        MaDV INT IDENTITY(1, 1) NOT NULL,
        TenDV NVARCHAR(100) NOT NULL,
        LoaiDV NVARCHAR(30) NOT NULL,
        DonViTinh NVARCHAR(20) NULL,
        DonGia DECIMAL(12, 2) NOT NULL,
        SoLuongTon INT NULL,
        MucTonChuan INT NULL,
        CONSTRAINT PK_DICH_VU PRIMARY KEY (MaDV),
        CONSTRAINT UQ_DICH_VU_TenDV UNIQUE (TenDV),
        CONSTRAINT CK_DICH_VU_Loai
            CHECK (LoaiDV IN (N'SanPham', N'NguQuaDem')),
        CONSTRAINT CK_DICH_VU_DonGia CHECK (DonGia >= 0),
        CONSTRAINT CK_DICH_VU_TonKho CHECK
        (
            (
                LoaiDV = N'SanPham'
                AND SoLuongTon IS NOT NULL
                AND MucTonChuan IS NOT NULL
                AND SoLuongTon >= 0
                AND MucTonChuan > 0
            )
            OR
            (
                LoaiDV = N'NguQuaDem'
                AND SoLuongTon IS NULL
                AND MucTonChuan IS NULL
            )
        )
    );

    CREATE TABLE dbo.PHIEN_SU_DUNG
    (
        MaPhien BIGINT IDENTITY(1, 1) NOT NULL,
        MaMay INT NOT NULL,
        MaTK UNIQUEIDENTIFIER NOT NULL,
        ThoiGianBatDau DATETIME2 NOT NULL,
        ThoiGianKetThuc DATETIME2 NULL,
        DonGiaGioApDung DECIMAL(12, 2) NOT NULL,
        TienGio DECIMAL(12, 2) NOT NULL
            CONSTRAINT DF_PHIEN_SU_DUNG_TienGio DEFAULT (0),
        TrangThai NVARCHAR(20) NOT NULL,
        LyDoKetThuc NVARCHAR(30) NULL,
        CONSTRAINT PK_PHIEN_SU_DUNG PRIMARY KEY (MaPhien),
        CONSTRAINT FK_PHIEN_SU_DUNG_MAY_TINH FOREIGN KEY (MaMay)
            REFERENCES dbo.MAY_TINH (MaMay),
        CONSTRAINT FK_PHIEN_SU_DUNG_TAI_KHOAN FOREIGN KEY (MaTK)
            REFERENCES dbo.TAI_KHOAN (MaTK),
        CONSTRAINT CK_PHIEN_SU_DUNG_TrangThai
            CHECK (TrangThai IN (N'DangSuDung', N'TamDung', N'KetThuc')),
        CONSTRAINT CK_PHIEN_SU_DUNG_DonGia
            CHECK (DonGiaGioApDung >= 0),
        CONSTRAINT CK_PHIEN_SU_DUNG_TienGio CHECK (TienGio >= 0)
    );

    CREATE INDEX IX_PHIEN_SU_DUNG_MaMay
        ON dbo.PHIEN_SU_DUNG (MaMay);
    CREATE INDEX IX_PHIEN_SU_DUNG_MaTK
        ON dbo.PHIEN_SU_DUNG (MaTK);

    CREATE TABLE dbo.CHI_TIET_DICH_VU
    (
        MaCTDV BIGINT IDENTITY(1, 1) NOT NULL,
        MaPhien BIGINT NOT NULL,
        MaDV INT NOT NULL,
        SoLuong INT NOT NULL,
        DonGiaApDung DECIMAL(12, 2) NOT NULL,
        ThanhTien AS
            (CONVERT(DECIMAL(12, 2), SoLuong * DonGiaApDung)) PERSISTED,
        CONSTRAINT PK_CHI_TIET_DICH_VU PRIMARY KEY (MaCTDV),
        CONSTRAINT FK_CHI_TIET_DICH_VU_PHIEN FOREIGN KEY (MaPhien)
            REFERENCES dbo.PHIEN_SU_DUNG (MaPhien)
            ON DELETE CASCADE,
        CONSTRAINT FK_CHI_TIET_DICH_VU_DICH_VU FOREIGN KEY (MaDV)
            REFERENCES dbo.DICH_VU (MaDV),
        CONSTRAINT CK_CHI_TIET_DICH_VU_SoLuong CHECK (SoLuong > 0),
        CONSTRAINT CK_CHI_TIET_DICH_VU_DonGia
            CHECK (DonGiaApDung >= 0)
    );

    CREATE INDEX IX_CHI_TIET_DICH_VU_MaPhien
        ON dbo.CHI_TIET_DICH_VU (MaPhien);
    CREATE INDEX IX_CHI_TIET_DICH_VU_MaDV
        ON dbo.CHI_TIET_DICH_VU (MaDV);

    CREATE TABLE dbo.HOA_DON
    (
        MaHD BIGINT IDENTITY(1, 1) NOT NULL,
        MaPhien BIGINT NOT NULL,
        TienGio DECIMAL(12, 2) NOT NULL,
        TienDichVu DECIMAL(12, 2) NOT NULL,
        GiamGia DECIMAL(12, 2) NOT NULL,
        TongTien DECIMAL(12, 2) NOT NULL,
        ThoiDiemLap DATETIME2 NOT NULL,
        TrangThai NVARCHAR(20) NOT NULL,
        CONSTRAINT PK_HOA_DON PRIMARY KEY (MaHD),
        CONSTRAINT UQ_HOA_DON_MaPhien UNIQUE (MaPhien),
        CONSTRAINT FK_HOA_DON_PHIEN FOREIGN KEY (MaPhien)
            REFERENCES dbo.PHIEN_SU_DUNG (MaPhien),
        CONSTRAINT CK_HOA_DON_TienGio CHECK (TienGio >= 0),
        CONSTRAINT CK_HOA_DON_TienDichVu CHECK (TienDichVu >= 0),
        CONSTRAINT CK_HOA_DON_GiamGia CHECK (GiamGia >= 0),
        CONSTRAINT CK_HOA_DON_TongTien CHECK (TongTien >= 0),
        CONSTRAINT CK_HOA_DON_TrangThai
            CHECK (TrangThai IN (N'ChuaThanhToan', N'DaThanhToan', N'CongNo'))
    );

    CREATE TABLE dbo.GIAO_DICH_NAP
    (
        MaNap BIGINT IDENTITY(1, 1) NOT NULL,
        MaTK UNIQUEIDENTIFIER NOT NULL,
        MaGoi INT NOT NULL,
        SoTien DECIMAL(12, 2) NOT NULL,
        SoGioNap INT NOT NULL,
        NguoiThucHien UNIQUEIDENTIFIER NOT NULL,
        ThoiDiem DATETIME2 NOT NULL,
        GhiChu NVARCHAR(500) NULL,
        CONSTRAINT PK_GIAO_DICH_NAP PRIMARY KEY (MaNap),
        CONSTRAINT FK_GIAO_DICH_NAP_TAI_KHOAN FOREIGN KEY (MaTK)
            REFERENCES dbo.TAI_KHOAN (MaTK),
        CONSTRAINT FK_GIAO_DICH_NAP_GOI_GIO FOREIGN KEY (MaGoi)
            REFERENCES dbo.GOI_GIO (MaGoi),
        CONSTRAINT FK_GIAO_DICH_NAP_NGUOI_THUC_HIEN
            FOREIGN KEY (NguoiThucHien)
            REFERENCES dbo.TAI_KHOAN (MaTK),
        CONSTRAINT CK_GIAO_DICH_NAP_SoTien CHECK (SoTien >= 0),
        CONSTRAINT CK_GIAO_DICH_NAP_SoGio CHECK (SoGioNap > 0)
    );

    CREATE INDEX IX_GIAO_DICH_NAP_MaTK
        ON dbo.GIAO_DICH_NAP (MaTK);
    CREATE INDEX IX_GIAO_DICH_NAP_MaGoi
        ON dbo.GIAO_DICH_NAP (MaGoi);
    CREATE INDEX IX_GIAO_DICH_NAP_NguoiThucHien
        ON dbo.GIAO_DICH_NAP (NguoiThucHien);

    CREATE TABLE dbo.PASSWORD_RESET_OTP
    (
        MaOTP BIGINT IDENTITY(1, 1) NOT NULL,
        MaTK UNIQUEIDENTIFIER NOT NULL,
        OTPHash VARBINARY(32) NOT NULL,
        ExpiresAt DATETIME2 NOT NULL,
        IsUsed BIT NOT NULL
            CONSTRAINT DF_PASSWORD_RESET_OTP_IsUsed DEFAULT (0),
        SoLanThuSai INT NOT NULL
            CONSTRAINT DF_PASSWORD_RESET_OTP_SoLanThuSai DEFAULT (0),
        CreatedAt DATETIME2 NOT NULL,
        CONSTRAINT PK_PASSWORD_RESET_OTP PRIMARY KEY (MaOTP),
        CONSTRAINT FK_PASSWORD_RESET_OTP_TAI_KHOAN FOREIGN KEY (MaTK)
            REFERENCES dbo.TAI_KHOAN (MaTK)
            ON DELETE CASCADE,
        CONSTRAINT CK_PASSWORD_RESET_OTP_SoLanThuSai
            CHECK (SoLanThuSai >= 0)
    );

    CREATE INDEX IX_PASSWORD_RESET_OTP_MaTK
        ON dbo.PASSWORD_RESET_OTP (MaTK);

    CREATE TABLE dbo.LENH_DIEU_KHIEN
    (
        MaLenh UNIQUEIDENTIFIER NOT NULL,
        MaMay INT NOT NULL,
        MaAdmin UNIQUEIDENTIFIER NOT NULL,
        LoaiLenh NVARCHAR(30) NOT NULL,
        DuLieu NVARCHAR(MAX) NULL,
        TrangThai NVARCHAR(20) NOT NULL,
        ThoiDiemGui DATETIME2 NOT NULL,
        ThoiDiemACK DATETIME2 NULL,
        ThoiDiemKetQua DATETIME2 NULL,
        MaLoi NVARCHAR(10) NULL,
        CONSTRAINT PK_LENH_DIEU_KHIEN PRIMARY KEY (MaLenh),
        CONSTRAINT FK_LENH_DIEU_KHIEN_MAY_TINH FOREIGN KEY (MaMay)
            REFERENCES dbo.MAY_TINH (MaMay),
        CONSTRAINT FK_LENH_DIEU_KHIEN_TAI_KHOAN FOREIGN KEY (MaAdmin)
            REFERENCES dbo.TAI_KHOAN (MaTK),
        CONSTRAINT CK_LENH_DIEU_KHIEN_TrangThai
            CHECK
            (
                TrangThai IN
                (
                    N'ChoGui',
                    N'DaGui',
                    N'DaACK',
                    N'ThanhCong',
                    N'ThatBai'
                )
            )
    );

    CREATE INDEX IX_LENH_DIEU_KHIEN_MaMay
        ON dbo.LENH_DIEU_KHIEN (MaMay);
    CREATE INDEX IX_LENH_DIEU_KHIEN_MaAdmin
        ON dbo.LENH_DIEU_KHIEN (MaAdmin);

    CREATE TABLE dbo.CHINH_SACH_CHAN
    (
        MaChinhSach INT IDENTITY(1, 1) NOT NULL,
        Loai NVARCHAR(20) NOT NULL,
        GiaTri NVARCHAR(255) NOT NULL,
        CheDo NVARCHAR(20) NOT NULL,
        TrangThai BIT NOT NULL
            CONSTRAINT DF_CHINH_SACH_CHAN_TrangThai DEFAULT (1),
        CreatedBy UNIQUEIDENTIFIER NOT NULL,
        CreatedAt DATETIME2 NOT NULL,
        CONSTRAINT PK_CHINH_SACH_CHAN PRIMARY KEY (MaChinhSach),
        CONSTRAINT UQ_CHINH_SACH_CHAN_Loai_GiaTri UNIQUE (Loai, GiaTri),
        CONSTRAINT FK_CHINH_SACH_CHAN_TAI_KHOAN FOREIGN KEY (CreatedBy)
            REFERENCES dbo.TAI_KHOAN (MaTK),
        CONSTRAINT CK_CHINH_SACH_CHAN_Loai
            CHECK (Loai IN (N'TienTrinh', N'Website')),
        CONSTRAINT CK_CHINH_SACH_CHAN_CheDo
            CHECK (CheDo IN (N'Chan', N'CanhBao'))
    );

    CREATE TABLE dbo.VI_PHAM_CHINH_SACH
    (
        MaViPham BIGINT IDENTITY(1, 1) NOT NULL,
        MaMay INT NOT NULL,
        MaTK UNIQUEIDENTIFIER NULL,
        MaChinhSach INT NOT NULL,
        GiaTriPhatHien NVARCHAR(255) NOT NULL,
        ThoiDiem DATETIME2 NOT NULL,
        HanhDongXuLy NVARCHAR(50) NOT NULL,
        CONSTRAINT PK_VI_PHAM_CHINH_SACH PRIMARY KEY (MaViPham),
        CONSTRAINT FK_VI_PHAM_CHINH_SACH_MAY_TINH FOREIGN KEY (MaMay)
            REFERENCES dbo.MAY_TINH (MaMay),
        CONSTRAINT FK_VI_PHAM_CHINH_SACH_TAI_KHOAN FOREIGN KEY (MaTK)
            REFERENCES dbo.TAI_KHOAN (MaTK),
        CONSTRAINT FK_VI_PHAM_CHINH_SACH_CHINH_SACH FOREIGN KEY (MaChinhSach)
            REFERENCES dbo.CHINH_SACH_CHAN (MaChinhSach)
    );

    CREATE INDEX IX_VI_PHAM_CHINH_SACH_MaMay
        ON dbo.VI_PHAM_CHINH_SACH (MaMay);
    CREATE INDEX IX_VI_PHAM_CHINH_SACH_MaTK
        ON dbo.VI_PHAM_CHINH_SACH (MaTK);
    CREATE INDEX IX_VI_PHAM_CHINH_SACH_MaChinhSach
        ON dbo.VI_PHAM_CHINH_SACH (MaChinhSach);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
