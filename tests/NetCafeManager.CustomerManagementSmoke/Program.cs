using NetCafeManager.Server.Customers;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Customers;

string connectionString = Environment.GetEnvironmentVariable("NETCAFE_CUSTOMER_SMOKE_CONNECTION")
    ?? throw new InvalidOperationException("Thiếu NETCAFE_CUSTOMER_SMOKE_CONNECTION.");

var administrator = new AuthenticatedUser(
    Guid.NewGuid(),
    "admin-smoke",
    UserRole.Administrator,
    0m);
var clientActor = new AuthenticatedUser(
    Guid.NewGuid(),
    "client-smoke",
    UserRole.Client,
    0m);
var service = new CustomerManagementService(connectionString);
string suffix = Guid.NewGuid().ToString("N")[..10];
var request = new CreateCustomerRequest(
    $"kh-{suffix}",
    $"kh-{suffix}@example.test",
    "Khách hàng kiểm thử",
    "MatKhau!123");

CustomerResult<CustomerSummary> forbidden = await service.CreateAsync(clientActor, request);
Assert(!forbidden.IsSuccess && forbidden.ErrorCode == "CUSTOMER_FORBIDDEN",
    "Client không được phép tạo khách hàng.");

CustomerResult<CustomerSummary> created = await service.CreateAsync(administrator, request);
if (!created.IsSuccess || created.Value is null)
{
    throw new InvalidOperationException("Quản trị viên phải tạo được khách hàng.");
}

CustomerSummary createdCustomer = created.Value;
Assert(createdCustomer.Balance == 0m && createdCustomer.IsActive,
    "Tài khoản mới phải có số dư 0 và đang hoạt động.");

CustomerResult<CustomerSummary> duplicate = await service.CreateAsync(administrator, request);
Assert(!duplicate.IsSuccess && duplicate.ErrorCode == "CUSTOMER_DUPLICATE",
    "Tên đăng nhập hoặc email trùng phải bị từ chối.");

CustomerResult<CustomerSummary> updated = await service.UpdateAsync(
    administrator,
    new UpdateCustomerRequest(
        createdCustomer.Id,
        $"updated-{suffix}@example.test",
        "Khách hàng đã cập nhật",
        false));
Assert(updated.IsSuccess && updated.Value is { IsActive: false },
    "Phải cập nhật được hồ sơ và trạng thái tài khoản.");

IReadOnlyList<CustomerSummary> customers = await service.ListAsync(administrator);
Assert(customers.Any(customer => customer.Id == createdCustomer.Id),
    "Danh sách phải chứa khách hàng vừa tạo.");

Console.WriteLine("Customer management smoke test passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
