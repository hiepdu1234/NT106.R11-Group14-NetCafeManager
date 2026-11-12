using System.Data;
using Microsoft.EntityFrameworkCore;
using NetCafeManager.Infrastructure.Persistence;
using NetCafeManager.Infrastructure.Persistence.Entities;
using NetCafeManager.Server.Authorization;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Invoices;

namespace NetCafeManager.Server.Invoices;

public sealed class InvoiceService
{
    private readonly DbContextOptions<NetCafeDbContext> _options;
    private readonly TimeProvider _timeProvider;

    public InvoiceService(string connectionString, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _options = new DbContextOptionsBuilder<NetCafeDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<InvoiceResult<InvoiceSummary>> CreateAsync(
        AuthenticatedUser actor,
        CreateInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return InvoiceResult<InvoiceSummary>.Failure(
                "INVOICE_FORBIDDEN",
                "Chỉ quản trị viên được lập hóa đơn.");
        }

        if (request.UsageSessionId <= 0 || request.Discount < 0)
        {
            return InvoiceResult<InvoiceSummary>.Failure(
                "INVOICE_INVALID_INPUT",
                "Mã phiên hoặc số tiền giảm giá không hợp lệ.");
        }

        await using var db = new NetCafeDbContext(_options);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        Invoice? existing = await db.Invoices
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.UsageSessionId == request.UsageSessionId,
                cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return InvoiceResult<InvoiceSummary>.Success(
                ToSummary(existing),
                "Phiên sử dụng đã có hóa đơn; hệ thống trả lại hóa đơn hiện có.");
        }

        UsageSession? session = await db.UsageSessions
            .Include(row => row.ServiceUsages)
            .SingleOrDefaultAsync(row => row.Id == request.UsageSessionId, cancellationToken);
        if (session is null)
        {
            return InvoiceResult<InvoiceSummary>.Failure(
                "INVOICE_SESSION_NOT_FOUND",
                "Không tìm thấy phiên sử dụng.");
        }

        if (!string.Equals(session.Status, "KetThuc", StringComparison.Ordinal))
        {
            return InvoiceResult<InvoiceSummary>.Failure(
                "INVOICE_SESSION_NOT_ENDED",
                "Chỉ lập hóa đơn sau khi phiên sử dụng đã kết thúc.");
        }

        decimal serviceCost = session.ServiceUsages.Sum(
            detail => checked(detail.Quantity * detail.UnitPriceApplied));
        decimal subtotal = checked(session.UsageCost + serviceCost);
        if (request.Discount > subtotal)
        {
            return InvoiceResult<InvoiceSummary>.Failure(
                "INVOICE_DISCOUNT_TOO_LARGE",
                "Giảm giá không được lớn hơn tổng tiền giờ và dịch vụ.");
        }

        var invoice = new Invoice
        {
            UsageSessionId = session.Id,
            UsageCost = session.UsageCost,
            ServiceCost = serviceCost,
            Discount = request.Discount,
            TotalAmount = subtotal - request.Discount,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            Status = InvoiceStatuses.Unpaid
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return InvoiceResult<InvoiceSummary>.Success(
            ToSummary(invoice),
            "Đã lập hóa đơn cho phiên sử dụng.");
    }

    public async Task<InvoiceResult<InvoiceSummary>> SetStatusAsync(
        AuthenticatedUser actor,
        long invoiceId,
        string status,
        CancellationToken cancellationToken = default)
    {
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return InvoiceResult<InvoiceSummary>.Failure(
                "INVOICE_FORBIDDEN",
                "Chỉ quản trị viên được cập nhật trạng thái hóa đơn.");
        }

        if (status is not (InvoiceStatuses.Paid or InvoiceStatuses.Debt))
        {
            return InvoiceResult<InvoiceSummary>.Failure(
                "INVOICE_INVALID_STATUS",
                "Trạng thái mới chỉ có thể là đã thanh toán hoặc công nợ.");
        }

        await using var db = new NetCafeDbContext(_options);
        Invoice? invoice = await db.Invoices.SingleOrDefaultAsync(
            row => row.Id == invoiceId,
            cancellationToken);
        if (invoice is null)
        {
            return InvoiceResult<InvoiceSummary>.Failure(
                "INVOICE_NOT_FOUND",
                "Không tìm thấy hóa đơn.");
        }

        if (invoice.Status == InvoiceStatuses.Paid)
        {
            return InvoiceResult<InvoiceSummary>.Failure(
                "INVOICE_ALREADY_PAID",
                "Hóa đơn đã thanh toán không thể chuyển trạng thái.");
        }

        invoice.Status = status;
        await db.SaveChangesAsync(cancellationToken);
        return InvoiceResult<InvoiceSummary>.Success(
            ToSummary(invoice),
            "Đã cập nhật trạng thái hóa đơn.");
    }

    public async Task<InvoiceResult<InvoiceSummary>> FindAsync(
        AuthenticatedUser actor,
        long invoiceId,
        CancellationToken cancellationToken = default)
    {
        if (!AdministratorGuard.IsAdministrator(actor))
        {
            return InvoiceResult<InvoiceSummary>.Failure(
                "INVOICE_FORBIDDEN",
                "Chỉ quản trị viên được xem hóa đơn theo mã.");
        }

        await using var db = new NetCafeDbContext(_options);
        Invoice? invoice = await db.Invoices
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == invoiceId, cancellationToken);
        return invoice is null
            ? InvoiceResult<InvoiceSummary>.Failure("INVOICE_NOT_FOUND", "Không tìm thấy hóa đơn.")
            : InvoiceResult<InvoiceSummary>.Success(ToSummary(invoice), "Đã tải hóa đơn.");
    }

    private static InvoiceSummary ToSummary(Invoice invoice) =>
        new(
            invoice.Id,
            invoice.UsageSessionId,
            invoice.UsageCost,
            invoice.ServiceCost,
            invoice.Discount,
            invoice.TotalAmount,
            new DateTimeOffset(invoice.CreatedAtUtc, TimeSpan.Zero),
            invoice.Status);
}
