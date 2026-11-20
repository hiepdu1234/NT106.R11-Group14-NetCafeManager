namespace NetCafeManager.Shared.Administration;

public sealed record CustomerIdRequest(Guid CustomerId);

public sealed record ServiceItemIdRequest(int ItemId);

public sealed record InvoiceStatusRequest(
    long InvoiceId,
    string? Status);
