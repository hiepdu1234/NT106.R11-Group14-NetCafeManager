using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Server.Authorization;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Billing;

namespace NetCafeManager.Server.Billing;

public sealed class HourPackageService
{
    private readonly DbContextOptions<NetCafeDbContext> _options;

    public HourPackageService(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
    }

    public async Task<IReadOnlyList<HourPackageSummary>> ListActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = new NetCafeDbContext(_options);
        return await db.HourPackages
            .AsNoTracking()
            .Where(package => package.IsActive)
            .OrderBy(package => package.Hours)
            .Select(package => new HourPackageSummary(
                package.Id,
                package.Name,
                package.Hours,
                package.Price,
                package.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<BillingResult<HourPackageSummary>> SaveAsync(
        AuthenticatedUser actor,
        SaveHourPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return BillingResult<HourPackageSummary>.Failure(
                "PACKAGE_FORBIDDEN",
                "Chỉ quản trị viên được cập nhật gói giờ.");
        }

        string name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100 || request.Hours <= 0 || request.Price <= 0)
        {
            return BillingResult<HourPackageSummary>.Failure(
                "PACKAGE_INVALID_INPUT",
                "Tên, số giờ hoặc giá tiền của gói không hợp lệ.");
        }

        await using var db = new NetCafeDbContext(_options);
        HourPackage? package = request.PackageId.HasValue
            ? await db.HourPackages.SingleOrDefaultAsync(
                row => row.Id == request.PackageId.Value,
                cancellationToken)
            : null;

        if (request.PackageId.HasValue && package is null)
        {
            return BillingResult<HourPackageSummary>.Failure(
                "PACKAGE_NOT_FOUND",
                "Không tìm thấy gói giờ.");
        }

        bool duplicate = await db.HourPackages.AnyAsync(
            row => row.Name == name && (!request.PackageId.HasValue || row.Id != request.PackageId.Value),
            cancellationToken);
        if (duplicate)
        {
            return BillingResult<HourPackageSummary>.Failure(
                "PACKAGE_DUPLICATE",
                "Tên gói giờ đã tồn tại.");
        }

        if (package is null)
        {
            package = new HourPackage
            {
                Name = name,
                Hours = request.Hours,
                Price = request.Price,
                IsActive = request.IsActive
            };
            db.HourPackages.Add(package);
        }
        else
        {
            package.Name = name;
            package.Hours = request.Hours;
            package.Price = request.Price;
            package.IsActive = request.IsActive;
        }

        await db.SaveChangesAsync(cancellationToken);
        var summary = new HourPackageSummary(
            package.Id,
            package.Name,
            package.Hours,
            package.Price,
            package.IsActive);
        return BillingResult<HourPackageSummary>.Success(summary, "Đã lưu gói giờ.");
    }
}
