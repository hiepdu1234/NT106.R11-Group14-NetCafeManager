using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence.Entities;

namespace NetCafeManager.Infrastructure.Persistence;

public sealed class NetCafeDbContext(DbContextOptions<NetCafeDbContext> options)
    : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<UsageSession> UsageSessions => Set<UsageSession>();
    public DbSet<ServiceItem> ServiceItems => Set<ServiceItem>();
    public DbSet<ServiceUsage> ServiceUsages => Set<ServiceUsage>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<HourPackage> HourPackages => Set<HourPackage>();
    public DbSet<TopUpTransaction> TopUpTransactions => Set<TopUpTransaction>();
    public DbSet<PasswordResetOtp> PasswordResetOtps => Set<PasswordResetOtp>();
    public DbSet<RemoteCommand> RemoteCommands => Set<RemoteCommand>();
    public DbSet<BlockPolicy> BlockPolicies => Set<BlockPolicy>();
    public DbSet<PolicyViolation> PolicyViolations => Set<PolicyViolation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAccounts(modelBuilder);
        ConfigureComputers(modelBuilder);
        ConfigureUsageSessions(modelBuilder);
        ConfigureServiceItems(modelBuilder);
        ConfigureServiceUsages(modelBuilder);
        ConfigureInvoices(modelBuilder);
        ConfigureHourPackages(modelBuilder);
        ConfigureTopUpTransactions(modelBuilder);
        ConfigurePasswordResetOtps(modelBuilder);
        ConfigureRemoteCommands(modelBuilder);
        ConfigureBlockPolicies(modelBuilder);
        ConfigurePolicyViolations(modelBuilder);
    }

    private static void ConfigureAccounts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("TAI_KHOAN", table =>
            {
                table.HasCheckConstraint(
                    "CK_TAI_KHOAN_VaiTro",
                    "[VaiTro] IN (N'Administrator', N'Client')");
                table.HasCheckConstraint("CK_TAI_KHOAN_SoDu", "[SoDu] >= 0");
                table.HasCheckConstraint(
                    "CK_TAI_KHOAN_IterationCount",
                    "[IterationCount] BETWEEN 1 AND 10000000");
            });

            entity.HasKey(account => account.Id);
            entity.Property(account => account.Id)
                .HasColumnName("MaTK")
                .ValueGeneratedNever();
            entity.Property(account => account.Username)
                .HasColumnName("TenDangNhap")
                .HasMaxLength(50)
                .IsRequired();
            entity.HasIndex(account => account.Username).IsUnique();
            entity.Property(account => account.PasswordAlgorithm)
                .HasColumnName("PasswordAlgorithm")
                .HasMaxLength(40)
                .IsRequired();
            entity.Property(account => account.PasswordIterations)
                .HasColumnName("IterationCount")
                .HasDefaultValue(100_000);
            entity.Property(account => account.PasswordSaltBase64)
                .HasColumnName("PasswordSaltBase64")
                .HasMaxLength(44)
                .IsUnicode(false)
                .IsRequired();
            entity.Property(account => account.PasswordHashBase64)
                .HasColumnName("PasswordHashBase64")
                .HasMaxLength(44)
                .IsUnicode(false)
                .IsRequired();
            entity.Property(account => account.Email)
                .HasColumnName("Email")
                .HasMaxLength(255)
                .IsRequired();
            entity.HasIndex(account => account.Email).IsUnique();
            entity.Property(account => account.FullName)
                .HasColumnName("HoTen")
                .HasMaxLength(100);
            entity.Property(account => account.Role)
                .HasColumnName("VaiTro")
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(account => account.Balance)
                .HasColumnName("SoDu")
                .HasPrecision(12, 2)
                .HasDefaultValue(0m);
            entity.Property(account => account.IsActive)
                .HasColumnName("TrangThai")
                .HasDefaultValue(true);
        });
    }

    private static void ConfigureComputers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Computer>(entity =>
        {
            entity.ToTable("MAY_TINH", table =>
                table.HasCheckConstraint(
                    "CK_MAY_TINH_TrangThai",
                    "[TrangThai] IN (N'Trong', N'DangSuDung', N'Khoa', N'BaoTri', N'SuCo', N'MatKetNoi')"));

            entity.HasKey(computer => computer.Id);
            entity.Property(computer => computer.Id)
                .HasColumnName("MaMay")
                .UseIdentityColumn();
            entity.Property(computer => computer.Name)
                .HasColumnName("TenMay")
                .HasMaxLength(50)
                .IsRequired();
            entity.HasIndex(computer => computer.Name).IsUnique();
            entity.Property(computer => computer.IpAddress)
                .HasColumnName("DiaChiIP")
                .HasMaxLength(45)
                .IsUnicode(false);
            entity.HasIndex(computer => computer.IpAddress)
                .IsUnique()
                .HasFilter("[DiaChiIP] IS NOT NULL");
            entity.Property(computer => computer.MacAddress)
                .HasColumnName("DiaChiMAC")
                .HasMaxLength(17)
                .IsUnicode(false);
            entity.HasIndex(computer => computer.MacAddress)
                .IsUnique()
                .HasFilter("[DiaChiMAC] IS NOT NULL");
            entity.Property(computer => computer.Status)
                .HasColumnName("TrangThai")
                .HasMaxLength(20)
                .HasDefaultValue("Trong")
                .IsRequired();
            entity.Property(computer => computer.LastHeartbeatAtUtc)
                .HasColumnName("LanHeartbeatCuoi")
                .HasColumnType("datetime2");
            entity.Property(computer => computer.MaintenanceReason)
                .HasColumnName("LyDoBaoTri")
                .HasMaxLength(500);
        });
    }

    private static void ConfigureUsageSessions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UsageSession>(entity =>
        {
            entity.ToTable("PHIEN_SU_DUNG", table =>
            {
                table.HasCheckConstraint(
                    "CK_PHIEN_SU_DUNG_TrangThai",
                    "[TrangThai] IN (N'DangSuDung', N'TamDung', N'KetThuc')");
                table.HasCheckConstraint("CK_PHIEN_SU_DUNG_DonGia", "[DonGiaGioApDung] >= 0");
                table.HasCheckConstraint("CK_PHIEN_SU_DUNG_TienGio", "[TienGio] >= 0");
            });

            entity.HasKey(session => session.Id);
            entity.Property(session => session.Id)
                .HasColumnName("MaPhien")
                .UseIdentityColumn();
            entity.Property(session => session.ComputerId).HasColumnName("MaMay");
            entity.Property(session => session.AccountId).HasColumnName("MaTK");
            entity.Property(session => session.StartedAtUtc)
                .HasColumnName("ThoiGianBatDau")
                .HasColumnType("datetime2");
            entity.Property(session => session.EndedAtUtc)
                .HasColumnName("ThoiGianKetThuc")
                .HasColumnType("datetime2");
            entity.Property(session => session.HourlyRateApplied)
                .HasColumnName("DonGiaGioApDung")
                .HasPrecision(12, 2);
            entity.Property(session => session.UsageCost)
                .HasColumnName("TienGio")
                .HasPrecision(12, 2)
                .HasDefaultValue(0m);
            entity.Property(session => session.Status)
                .HasColumnName("TrangThai")
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(session => session.EndReason)
                .HasColumnName("LyDoKetThuc")
                .HasMaxLength(30);

            entity.HasOne(session => session.Computer)
                .WithMany(computer => computer.UsageSessions)
                .HasForeignKey(session => session.ComputerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(session => session.Account)
                .WithMany(account => account.UsageSessions)
                .HasForeignKey(session => session.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureServiceItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServiceItem>(entity =>
        {
            entity.ToTable("DICH_VU", table =>
            {
                table.HasCheckConstraint(
                    "CK_DICH_VU_Loai",
                    "[LoaiDV] IN (N'SanPham', N'NguQuaDem')");
                table.HasCheckConstraint("CK_DICH_VU_DonGia", "[DonGia] >= 0");
                table.HasCheckConstraint(
                    "CK_DICH_VU_TonKho",
                    "([LoaiDV] = N'SanPham' AND [SoLuongTon] IS NOT NULL "
                    + "AND [MucTonChuan] IS NOT NULL AND [SoLuongTon] >= 0 AND [MucTonChuan] > 0) "
                    + "OR ([LoaiDV] = N'NguQuaDem' AND [SoLuongTon] IS NULL AND [MucTonChuan] IS NULL)");
            });

            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id)
                .HasColumnName("MaDV")
                .UseIdentityColumn();
            entity.Property(item => item.Name)
                .HasColumnName("TenDV")
                .HasMaxLength(100)
                .IsRequired();
            entity.HasIndex(item => item.Name).IsUnique();
            entity.Property(item => item.Type)
                .HasColumnName("LoaiDV")
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(item => item.Unit)
                .HasColumnName("DonViTinh")
                .HasMaxLength(20);
            entity.Property(item => item.UnitPrice)
                .HasColumnName("DonGia")
                .HasPrecision(12, 2);
            entity.Property(item => item.StockQuantity)
                .HasColumnName("SoLuongTon");
            entity.Property(item => item.ReferenceStockQuantity)
                .HasColumnName("MucTonChuan");
        });
    }

    private static void ConfigureServiceUsages(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServiceUsage>(entity =>
        {
            entity.ToTable("CHI_TIET_DICH_VU", table =>
            {
                table.HasCheckConstraint("CK_CHI_TIET_DICH_VU_SoLuong", "[SoLuong] > 0");
                table.HasCheckConstraint("CK_CHI_TIET_DICH_VU_DonGia", "[DonGiaApDung] >= 0");
            });

            entity.HasKey(detail => detail.Id);
            entity.Property(detail => detail.Id)
                .HasColumnName("MaCTDV")
                .UseIdentityColumn();
            entity.Property(detail => detail.UsageSessionId).HasColumnName("MaPhien");
            entity.Property(detail => detail.ServiceItemId).HasColumnName("MaDV");
            entity.Property(detail => detail.Quantity).HasColumnName("SoLuong");
            entity.Property(detail => detail.UnitPriceApplied)
                .HasColumnName("DonGiaApDung")
                .HasPrecision(12, 2);
            entity.Property(detail => detail.TotalAmount)
                .HasColumnName("ThanhTien")
                .HasPrecision(12, 2)
                .HasComputedColumnSql(
                    "CONVERT(decimal(12,2), [SoLuong] * [DonGiaApDung])",
                    stored: true);

            entity.HasOne(detail => detail.UsageSession)
                .WithMany(session => session.ServiceUsages)
                .HasForeignKey(detail => detail.UsageSessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(detail => detail.ServiceItem)
                .WithMany(item => item.ServiceUsages)
                .HasForeignKey(detail => detail.ServiceItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureInvoices(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("HOA_DON", table =>
            {
                table.HasCheckConstraint("CK_HOA_DON_TienGio", "[TienGio] >= 0");
                table.HasCheckConstraint("CK_HOA_DON_TienDichVu", "[TienDichVu] >= 0");
                table.HasCheckConstraint("CK_HOA_DON_GiamGia", "[GiamGia] >= 0");
                table.HasCheckConstraint("CK_HOA_DON_TongTien", "[TongTien] >= 0");
                table.HasCheckConstraint(
                    "CK_HOA_DON_TrangThai",
                    "[TrangThai] IN (N'ChuaThanhToan', N'DaThanhToan', N'CongNo')");
            });

            entity.HasKey(invoice => invoice.Id);
            entity.Property(invoice => invoice.Id)
                .HasColumnName("MaHD")
                .UseIdentityColumn();
            entity.Property(invoice => invoice.UsageSessionId).HasColumnName("MaPhien");
            entity.HasIndex(invoice => invoice.UsageSessionId).IsUnique();
            entity.Property(invoice => invoice.UsageCost)
                .HasColumnName("TienGio")
                .HasPrecision(12, 2);
            entity.Property(invoice => invoice.ServiceCost)
                .HasColumnName("TienDichVu")
                .HasPrecision(12, 2);
            entity.Property(invoice => invoice.Discount)
                .HasColumnName("GiamGia")
                .HasPrecision(12, 2);
            entity.Property(invoice => invoice.TotalAmount)
                .HasColumnName("TongTien")
                .HasPrecision(12, 2);
            entity.Property(invoice => invoice.CreatedAtUtc)
                .HasColumnName("ThoiDiemLap")
                .HasColumnType("datetime2");
            entity.Property(invoice => invoice.Status)
                .HasColumnName("TrangThai")
                .HasMaxLength(20)
                .IsRequired();

            entity.HasOne(invoice => invoice.UsageSession)
                .WithOne(session => session.Invoice)
                .HasForeignKey<Invoice>(invoice => invoice.UsageSessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureHourPackages(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HourPackage>(entity =>
        {
            entity.ToTable("GOI_GIO", table =>
            {
                table.HasCheckConstraint("CK_GOI_GIO_SoGio", "[SoGio] > 0");
                table.HasCheckConstraint("CK_GOI_GIO_GiaTien", "[GiaTien] >= 0");
            });

            entity.HasKey(package => package.Id);
            entity.Property(package => package.Id)
                .HasColumnName("MaGoi")
                .UseIdentityColumn();
            entity.Property(package => package.Name)
                .HasColumnName("TenGoi")
                .HasMaxLength(100)
                .IsRequired();
            entity.HasIndex(package => package.Name).IsUnique();
            entity.Property(package => package.Hours).HasColumnName("SoGio");
            entity.Property(package => package.Price)
                .HasColumnName("GiaTien")
                .HasPrecision(12, 2);
            entity.Property(package => package.IsActive)
                .HasColumnName("TrangThai")
                .HasDefaultValue(true);
        });
    }

    private static void ConfigureTopUpTransactions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TopUpTransaction>(entity =>
        {
            entity.ToTable("GIAO_DICH_NAP", table =>
            {
                table.HasCheckConstraint("CK_GIAO_DICH_NAP_SoTien", "[SoTien] >= 0");
                table.HasCheckConstraint("CK_GIAO_DICH_NAP_SoGio", "[SoGioNap] > 0");
            });

            entity.HasKey(transaction => transaction.Id);
            entity.Property(transaction => transaction.Id)
                .HasColumnName("MaNap")
                .UseIdentityColumn();
            entity.Property(transaction => transaction.AccountId).HasColumnName("MaTK");
            entity.Property(transaction => transaction.HourPackageId).HasColumnName("MaGoi");
            entity.Property(transaction => transaction.Amount)
                .HasColumnName("SoTien")
                .HasPrecision(12, 2);
            entity.Property(transaction => transaction.HoursAdded).HasColumnName("SoGioNap");
            entity.Property(transaction => transaction.PerformedByAccountId)
                .HasColumnName("NguoiThucHien");
            entity.Property(transaction => transaction.CreatedAtUtc)
                .HasColumnName("ThoiDiem")
                .HasColumnType("datetime2");
            entity.Property(transaction => transaction.Note)
                .HasColumnName("GhiChu")
                .HasMaxLength(500);

            entity.HasOne(transaction => transaction.Account)
                .WithMany(account => account.ReceivedTopUps)
                .HasForeignKey(transaction => transaction.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(transaction => transaction.PerformedByAccount)
                .WithMany(account => account.PerformedTopUps)
                .HasForeignKey(transaction => transaction.PerformedByAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(transaction => transaction.HourPackage)
                .WithMany(package => package.TopUpTransactions)
                .HasForeignKey(transaction => transaction.HourPackageId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurePasswordResetOtps(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PasswordResetOtp>(entity =>
        {
            entity.ToTable("PASSWORD_RESET_OTP", table =>
                table.HasCheckConstraint("CK_PASSWORD_RESET_OTP_SoLanThuSai", "[SoLanThuSai] >= 0"));

            entity.HasKey(otp => otp.Id);
            entity.Property(otp => otp.Id)
                .HasColumnName("MaOTP")
                .UseIdentityColumn();
            entity.Property(otp => otp.AccountId).HasColumnName("MaTK");
            entity.Property(otp => otp.OtpHash)
                .HasColumnName("OTPHash")
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(otp => otp.ExpiresAtUtc)
                .HasColumnName("ExpiresAt")
                .HasColumnType("datetime2");
            entity.Property(otp => otp.IsUsed)
                .HasColumnName("IsUsed")
                .HasDefaultValue(false);
            entity.Property(otp => otp.FailedAttemptCount)
                .HasColumnName("SoLanThuSai")
                .HasDefaultValue(0);
            entity.Property(otp => otp.CreatedAtUtc)
                .HasColumnName("CreatedAt")
                .HasColumnType("datetime2");

            entity.HasOne(otp => otp.Account)
                .WithMany(account => account.PasswordResetOtps)
                .HasForeignKey(otp => otp.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureRemoteCommands(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RemoteCommand>(entity =>
        {
            entity.ToTable("LENH_DIEU_KHIEN", table =>
                table.HasCheckConstraint(
                    "CK_LENH_DIEU_KHIEN_TrangThai",
                    "[TrangThai] IN (N'ChoGui', N'DaGui', N'DaACK', N'ThanhCong', N'ThatBai')"));

            entity.HasKey(command => command.Id);
            entity.Property(command => command.Id)
                .HasColumnName("MaLenh")
                .ValueGeneratedNever();
            entity.Property(command => command.ComputerId).HasColumnName("MaMay");
            entity.Property(command => command.AdministratorAccountId).HasColumnName("MaAdmin");
            entity.Property(command => command.CommandType)
                .HasColumnName("LoaiLenh")
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(command => command.Payload)
                .HasColumnName("DuLieu")
                .HasColumnType("nvarchar(max)");
            entity.Property(command => command.Status)
                .HasColumnName("TrangThai")
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(command => command.SentAtUtc)
                .HasColumnName("ThoiDiemGui")
                .HasColumnType("datetime2");
            entity.Property(command => command.AcknowledgedAtUtc)
                .HasColumnName("ThoiDiemACK")
                .HasColumnType("datetime2");
            entity.Property(command => command.CompletedAtUtc)
                .HasColumnName("ThoiDiemKetQua")
                .HasColumnType("datetime2");
            entity.Property(command => command.ErrorCode)
                .HasColumnName("MaLoi")
                .HasMaxLength(10);

            entity.HasOne(command => command.Computer)
                .WithMany(computer => computer.RemoteCommands)
                .HasForeignKey(command => command.ComputerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(command => command.AdministratorAccount)
                .WithMany(account => account.RemoteCommands)
                .HasForeignKey(command => command.AdministratorAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureBlockPolicies(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BlockPolicy>(entity =>
        {
            entity.ToTable("CHINH_SACH_CHAN", table =>
            {
                table.HasCheckConstraint(
                    "CK_CHINH_SACH_CHAN_Loai",
                    "[Loai] IN (N'TienTrinh', N'Website')");
                table.HasCheckConstraint(
                    "CK_CHINH_SACH_CHAN_CheDo",
                    "[CheDo] IN (N'Chan', N'CanhBao')");
            });

            entity.HasKey(policy => policy.Id);
            entity.Property(policy => policy.Id)
                .HasColumnName("MaChinhSach")
                .UseIdentityColumn();
            entity.Property(policy => policy.Type)
                .HasColumnName("Loai")
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(policy => policy.Value)
                .HasColumnName("GiaTri")
                .HasMaxLength(255)
                .IsRequired();
            entity.Property(policy => policy.Mode)
                .HasColumnName("CheDo")
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(policy => policy.IsActive)
                .HasColumnName("TrangThai")
                .HasDefaultValue(true);
            entity.Property(policy => policy.CreatedByAccountId).HasColumnName("CreatedBy");
            entity.Property(policy => policy.CreatedAtUtc)
                .HasColumnName("CreatedAt")
                .HasColumnType("datetime2");

            entity.HasIndex(policy => new { policy.Type, policy.Value }).IsUnique();
            entity.HasOne(policy => policy.CreatedByAccount)
                .WithMany(account => account.CreatedBlockPolicies)
                .HasForeignKey(policy => policy.CreatedByAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurePolicyViolations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PolicyViolation>(entity =>
        {
            entity.ToTable("VI_PHAM_CHINH_SACH");

            entity.HasKey(violation => violation.Id);
            entity.Property(violation => violation.Id)
                .HasColumnName("MaViPham")
                .UseIdentityColumn();
            entity.Property(violation => violation.ComputerId).HasColumnName("MaMay");
            entity.Property(violation => violation.AccountId).HasColumnName("MaTK");
            entity.Property(violation => violation.BlockPolicyId).HasColumnName("MaChinhSach");
            entity.Property(violation => violation.DetectedValue)
                .HasColumnName("GiaTriPhatHien")
                .HasMaxLength(255)
                .IsRequired();
            entity.Property(violation => violation.DetectedAtUtc)
                .HasColumnName("ThoiDiem")
                .HasColumnType("datetime2");
            entity.Property(violation => violation.ActionTaken)
                .HasColumnName("HanhDongXuLy")
                .HasMaxLength(50)
                .IsRequired();

            entity.HasOne(violation => violation.Computer)
                .WithMany(computer => computer.PolicyViolations)
                .HasForeignKey(violation => violation.ComputerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(violation => violation.Account)
                .WithMany(account => account.PolicyViolations)
                .HasForeignKey(violation => violation.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(violation => violation.BlockPolicy)
                .WithMany(policy => policy.PolicyViolations)
                .HasForeignKey(violation => violation.BlockPolicyId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
