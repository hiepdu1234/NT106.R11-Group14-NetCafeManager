using System.Text.Json;
using NetCafeManager.Server.Administration;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Server.Billing;
using NetCafeManager.Server.Customers;
using NetCafeManager.Server.Inventory;
using NetCafeManager.Server.Invoices;
using NetCafeManager.Server.Machines;
using NetCafeManager.Shared.Administration;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Billing;
using NetCafeManager.Shared.Customers;
using NetCafeManager.Shared.Inventory;
using NetCafeManager.Shared.Invoices;
using NetCafeManager.Shared.Networking;

namespace NetCafeManager.Server.Networking;

public sealed class AdminOperationsTcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> SupportedTypes =
    [
        NetworkMessageTypes.AdminCustomerList,
        NetworkMessageTypes.AdminCustomerCreate,
        NetworkMessageTypes.AdminCustomerUpdate,
        NetworkMessageTypes.AdminHourPackageList,
        NetworkMessageTypes.AdminHourPackageSave,
        NetworkMessageTypes.AdminTopUp,
        NetworkMessageTypes.AdminTopUpHistory,
        NetworkMessageTypes.AdminInventoryList,
        NetworkMessageTypes.AdminInventorySave,
        NetworkMessageTypes.AdminInventoryAdjust,
        NetworkMessageTypes.AdminInventoryDelete,
        NetworkMessageTypes.AdminInvoiceList,
        NetworkMessageTypes.AdminInvoiceCreate,
        NetworkMessageTypes.AdminInvoiceSetStatus,
        NetworkMessageTypes.AdminRefundCreate,
        NetworkMessageTypes.AdminRefundHistory,
        NetworkMessageTypes.AdminMachineList,
        NetworkMessageTypes.AdminMachineSetStatus,
        NetworkMessageTypes.AdminMachineHistoryList,
        NetworkMessageTypes.AdminRemoteCommandQueue,
        NetworkMessageTypes.AdminRemoteCommandHistory,
        NetworkMessageTypes.AdminAuditList
    ];

    private readonly CustomerManagementService _customers;
    private readonly HourPackageService _hourPackages;
    private readonly TopUpService _topUps;
    private readonly ProductInventoryService _inventory;
    private readonly InvoiceService _invoices;
    private readonly BalanceRefundService _refunds;
    private readonly AuthenticationService _authentication;
    private readonly MachineAdministrationService? _machines;
    private readonly RemoteCommandService? _remoteCommands;
    private readonly AdministrativeAuditService? _audit;

    public AdminOperationsTcpHandler(
        CustomerManagementService customers,
        HourPackageService hourPackages,
        TopUpService topUps,
        ProductInventoryService inventory,
        InvoiceService invoices,
        BalanceRefundService refunds,
        AuthenticationService authentication,
        MachineAdministrationService? machines = null,
        RemoteCommandService? remoteCommands = null,
        AdministrativeAuditService? audit = null)
    {
        _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        _hourPackages = hourPackages ?? throw new ArgumentNullException(nameof(hourPackages));
        _topUps = topUps ?? throw new ArgumentNullException(nameof(topUps));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _invoices = invoices ?? throw new ArgumentNullException(nameof(invoices));
        _refunds = refunds ?? throw new ArgumentNullException(nameof(refunds));
        _authentication = authentication
            ?? throw new ArgumentNullException(nameof(authentication));
        _machines = machines;
        _remoteCommands = remoteCommands;
        _audit = audit;
    }

    public async Task<NetworkMessage?> HandleAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!SupportedTypes.Contains(request.Type))
        {
            return null;
        }

        if (!TryValidateAdministrator(context))
        {
            return NetworkResponseFactory.Error(
                request,
                "ADMIN_AUTHORIZATION_REQUIRED",
                "Cần đăng nhập bằng tài khoản quản trị viên để thực hiện thao tác này.");
        }

        if (IsMachineAdministrationMessage(request.Type) &&
            (_machines is null || _remoteCommands is null || _audit is null))
        {
            return NetworkResponseFactory.Error(
                request,
                "ADMIN_MACHINE_NOT_CONFIGURED",
                "Server chưa cấu hình chức năng quản trị máy trạm.");
        }

        try
        {
            return request.Type switch
            {
                NetworkMessageTypes.AdminCustomerList =>
                    NetworkResponseFactory.Result(
                        request,
                        await _customers.ListAsync(context.User!, cancellationToken)),
                NetworkMessageTypes.AdminCustomerCreate =>
                    NetworkResponseFactory.Result(
                        request,
                        await _customers.CreateAsync(
                            context.User!,
                            Deserialize<CreateCustomerRequest>(request),
                            cancellationToken)),
                NetworkMessageTypes.AdminCustomerUpdate =>
                    NetworkResponseFactory.Result(
                        request,
                        await _customers.UpdateAsync(
                            context.User!,
                            Deserialize<UpdateCustomerRequest>(request),
                            cancellationToken)),
                NetworkMessageTypes.AdminHourPackageList =>
                    NetworkResponseFactory.Result(
                        request,
                        await _hourPackages.ListForAdministrationAsync(
                            context.User!,
                            cancellationToken)),
                NetworkMessageTypes.AdminHourPackageSave =>
                    NetworkResponseFactory.Result(
                        request,
                        await _hourPackages.SaveAsync(
                            context.User!,
                            Deserialize<SaveHourPackageRequest>(request),
                            cancellationToken)),
                NetworkMessageTypes.AdminTopUp =>
                    NetworkResponseFactory.Result(
                        request,
                        await _topUps.TopUpAsync(
                            context.User!,
                            Deserialize<TopUpRequest>(request),
                            cancellationToken)),
                NetworkMessageTypes.AdminTopUpHistory =>
                    NetworkResponseFactory.Result(
                        request,
                        await _topUps.GetHistoryAsync(
                            context.User!,
                            Deserialize<CustomerIdRequest>(request).CustomerId,
                            cancellationToken)),
                NetworkMessageTypes.AdminInventoryList =>
                    NetworkResponseFactory.Result(
                        request,
                        await _inventory.ListAsync(context.User!, cancellationToken)),
                NetworkMessageTypes.AdminInventorySave =>
                    NetworkResponseFactory.Result(
                        request,
                        await _inventory.SaveAsync(
                            context.User!,
                            Deserialize<SaveServiceItemRequest>(request),
                            cancellationToken)),
                NetworkMessageTypes.AdminInventoryAdjust =>
                    NetworkResponseFactory.Result(
                        request,
                        await _inventory.AdjustStockAsync(
                            context.User!,
                            Deserialize<AdjustStockRequest>(request),
                            cancellationToken)),
                NetworkMessageTypes.AdminInventoryDelete =>
                    NetworkResponseFactory.Result(
                        request,
                        await _inventory.DeleteAsync(
                            context.User!,
                            Deserialize<ServiceItemIdRequest>(request).ItemId,
                            cancellationToken)),
                NetworkMessageTypes.AdminInvoiceList =>
                    NetworkResponseFactory.Result(
                        request,
                        await _invoices.ListAsync(context.User!, cancellationToken: cancellationToken)),
                NetworkMessageTypes.AdminInvoiceCreate =>
                    NetworkResponseFactory.Result(
                        request,
                        await _invoices.CreateAsync(
                            context.User!,
                            Deserialize<CreateInvoiceRequest>(request),
                            cancellationToken)),
                NetworkMessageTypes.AdminInvoiceSetStatus =>
                    await SetInvoiceStatusAsync(request, context, cancellationToken),
                NetworkMessageTypes.AdminRefundCreate =>
                    NetworkResponseFactory.Result(
                        request,
                        await _refunds.RefundAllAndCloseAsync(
                            context.User!,
                            Deserialize<BalanceRefundRequest>(request),
                            cancellationToken)),
                NetworkMessageTypes.AdminRefundHistory =>
                    NetworkResponseFactory.Result(
                        request,
                        await _refunds.GetHistoryAsync(
                            context.User!,
                            Deserialize<CustomerIdRequest>(request).CustomerId,
                            cancellationToken)),
                NetworkMessageTypes.AdminMachineList =>
                    NetworkResponseFactory.Result(
                        request,
                        await _machines!.ListAsync(
                            context.User!,
                            cancellationToken)),
                NetworkMessageTypes.AdminMachineSetStatus =>
                    await SetMachineStatusAsync(
                        request,
                        context,
                        cancellationToken),
                NetworkMessageTypes.AdminMachineHistoryList =>
                    NetworkResponseFactory.Result(
                        request,
                        await _machines!.ListHistoryAsync(
                            context.User!,
                            Deserialize<MachineStatusHistoryRequest>(request).ComputerId,
                            cancellationToken)),
                NetworkMessageTypes.AdminRemoteCommandQueue =>
                    await QueueRemoteCommandAsync(
                        request,
                        context,
                        cancellationToken),
                NetworkMessageTypes.AdminRemoteCommandHistory =>
                    NetworkResponseFactory.Result(
                        request,
                        await _remoteCommands!.ListAsync(
                            context.User!,
                            Deserialize<RemoteCommandHistoryRequest>(request).ComputerId,
                            cancellationToken)),
                NetworkMessageTypes.AdminAuditList =>
                    NetworkResponseFactory.Result(
                        request,
                        await _audit!.ListRecentAsync(
                            context.User!,
                            cancellationToken)),
                _ => null
            };
        }
        catch (JsonException)
        {
            return NetworkResponseFactory.Error(
                request,
                "ADMIN_INVALID_REQUEST",
                "Dữ liệu thao tác quản trị không hợp lệ.");
        }
        catch (UnauthorizedAccessException)
        {
            return NetworkResponseFactory.Error(
                request,
                "ADMIN_FORBIDDEN",
                "Tài khoản hiện tại không có quyền quản trị.");
        }
    }

    private async Task<NetworkMessage> SetMachineStatusAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        SetMachineStatusRequest payload =
            Deserialize<SetMachineStatusRequest>(request);
        AdminResult<MachineAdminSummary> result =
            await _machines!.SetStatusAsync(
                context.User!,
                payload,
                cancellationToken);
        await _audit!.TryRecordAsync(
            context.User!,
            request.Type,
            "Computer",
            payload.ComputerId.ToString(),
            result.IsSuccess,
            result.Message,
            cancellationToken);
        return NetworkResponseFactory.Result(request, result);
    }

    private async Task<NetworkMessage> QueueRemoteCommandAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        QueueRemoteCommandRequest payload =
            Deserialize<QueueRemoteCommandRequest>(request);
        AdminResult<RemoteCommandSummary> result =
            await _remoteCommands!.QueueAsync(
                context.User!,
                payload,
                cancellationToken);
        await _audit!.TryRecordAsync(
            context.User!,
            request.Type,
            "Computer",
            payload.ComputerId.ToString(),
            result.IsSuccess,
            result.Message,
            cancellationToken);
        return NetworkResponseFactory.Result(request, result);
    }

    private async Task<NetworkMessage> SetInvoiceStatusAsync(
        NetworkMessage request,
        TcpConnectionContext context,
        CancellationToken cancellationToken)
    {
        InvoiceStatusRequest payload = Deserialize<InvoiceStatusRequest>(request);
        return NetworkResponseFactory.Result(
            request,
            await _invoices.SetStatusAsync(
                context.User!,
                payload.InvoiceId,
                payload.Status?.Trim() ?? string.Empty,
                cancellationToken));
    }

    private bool TryValidateAdministrator(TcpConnectionContext context)
    {
        return context.User?.Role == UserRole.Administrator &&
               !string.IsNullOrWhiteSpace(context.SessionToken) &&
               _authentication.TryValidateSession(
                   context.SessionToken,
                   out UserSession? session) &&
               session?.UserId == context.User.Id;
    }

    private static T Deserialize<T>(NetworkMessage request)
    {
        return request.Data.Deserialize<T>(JsonOptions)
            ?? throw new JsonException(
                $"Không đọc được dữ liệu {typeof(T).Name}.");
    }

    private static bool IsMachineAdministrationMessage(string type) => type is
        NetworkMessageTypes.AdminMachineList or
        NetworkMessageTypes.AdminMachineSetStatus or
        NetworkMessageTypes.AdminMachineHistoryList or
        NetworkMessageTypes.AdminRemoteCommandQueue or
        NetworkMessageTypes.AdminRemoteCommandHistory or
        NetworkMessageTypes.AdminAuditList;
}
