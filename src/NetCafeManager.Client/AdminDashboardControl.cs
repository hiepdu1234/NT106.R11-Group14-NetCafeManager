using System.Text.Json;
using NetCafeManager.Shared.Administration;
using NetCafeManager.Shared.Billing;
using NetCafeManager.Shared.Customers;
using NetCafeManager.Shared.Inventory;
using NetCafeManager.Shared.Invoices;
using NetCafeManager.Shared.Networking;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Client;

internal sealed class AdminDashboardControl : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly Func<string, object, CancellationToken, Task<JsonElement>> _send;
    private readonly Action<string, bool> _setStatus;
    private readonly DataGridView _customers = CreateGrid();
    private readonly DataGridView _packages = CreateGrid();
    private readonly DataGridView _inventory = CreateGrid();
    private readonly DataGridView _invoices = CreateGrid();
    private readonly DataGridView _machines = CreateGrid();
    private readonly DataGridView _audit = CreateGrid();
    private readonly List<HourPackageSummary> _packageItems = [];
    private int _busy;

    public AdminDashboardControl(
        Func<string, object, CancellationToken, Task<JsonElement>> send,
        Action<string, bool> setStatus)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        Dock = DockStyle.Fill;
        Height = 465;
        MinimumSize = new Size(760, 430);
        BuildLayout();
    }

    public async Task<IReadOnlyList<string>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        await TryLoadSectionAsync("khách hàng", LoadCustomersAsync, failures, cancellationToken);
        await TryLoadSectionAsync("gói giờ", LoadPackagesAsync, failures, cancellationToken);
        await TryLoadSectionAsync("kho / sản phẩm", LoadInventoryAsync, failures, cancellationToken);
        await TryLoadSectionAsync("hóa đơn", LoadInvoicesAsync, failures, cancellationToken);
        await TryLoadSectionAsync("máy trạm", LoadMachinesAsync, failures, cancellationToken);
        await TryLoadSectionAsync("nhật ký", LoadAuditAsync, failures, cancellationToken);
        return failures;
    }

    private static async Task TryLoadSectionAsync(
        string sectionName,
        Func<CancellationToken, Task> load,
        ICollection<string> failures,
        CancellationToken cancellationToken)
    {
        try
        {
            await load(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            failures.Add($"{sectionName}: yêu cầu bị hủy hoặc hết thời gian");
        }
        catch (Exception exception)
        {
            failures.Add($"{sectionName}: {exception.Message}");
        }
    }

    public void Clear()
    {
        _customers.Rows.Clear();
        _packages.Rows.Clear();
        _inventory.Rows.Clear();
        _invoices.Rows.Clear();
        _machines.Rows.Clear();
        _audit.Rows.Clear();
        _packageItems.Clear();
    }

    private void BuildLayout()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.5f)
        };
        tabs.TabPages.Add(BuildCustomerTab());
        tabs.TabPages.Add(BuildPackageTab());
        tabs.TabPages.Add(BuildInventoryTab());
        tabs.TabPages.Add(BuildInvoiceTab());
        tabs.TabPages.Add(BuildMachineTab());
        tabs.TabPages.Add(BuildAuditTab());
        Controls.Add(tabs);
    }

    private TabPage BuildCustomerTab()
    {
        ConfigureColumns(_customers,
            ("Id", "Mã tài khoản", 190),
            ("Username", "Tên đăng nhập", 120),
            ("FullName", "Họ tên", 150),
            ("Email", "Email", 180),
            ("Balance", "Số dư", 95),
            ("IsActive", "Hoạt động", 75));

        return CreateTab(
            "Khách hàng",
            _customers,
            ("Làm mới", () => LoadCustomersAsync()),
            ("Tạo tài khoản", CreateCustomerAsync),
            ("Sửa / khóa", UpdateCustomerAsync),
            ("Nạp gói giờ", TopUpAsync),
            ("Hoàn tiền & đóng", RefundAsync),
            ("Lịch sử", ShowCustomerHistoryAsync));
    }

    private TabPage BuildPackageTab()
    {
        ConfigureColumns(_packages,
            ("Id", "Mã", 60),
            ("Name", "Tên gói", 220),
            ("Hours", "Số giờ", 90),
            ("Price", "Giá", 120),
            ("IsActive", "Đang áp dụng", 100));

        return CreateTab(
            "Gói giờ",
            _packages,
            ("Làm mới", () => LoadPackagesAsync()),
            ("Thêm gói", () => SavePackageAsync(null)),
            ("Sửa gói", () => SavePackageAsync(SelectedPackage())));
    }

    private TabPage BuildInventoryTab()
    {
        ConfigureColumns(_inventory,
            ("Id", "Mã", 55),
            ("Name", "Tên dịch vụ", 180),
            ("Type", "Loại", 100),
            ("Unit", "Đơn vị", 75),
            ("UnitPrice", "Đơn giá", 100),
            ("Stock", "Tồn", 70),
            ("Reference", "Mức chuẩn", 85),
            ("State", "Trạng thái", 90));

        return CreateTab(
            "Kho / sản phẩm",
            _inventory,
            ("Làm mới", () => LoadInventoryAsync()),
            ("Thêm", () => SaveInventoryAsync(null)),
            ("Sửa", () => SaveInventoryAsync(SelectedInventoryItem())),
            ("Nhập / xuất kho", AdjustInventoryAsync),
            ("Xóa", DeleteInventoryAsync));
    }

    private TabPage BuildInvoiceTab()
    {
        ConfigureColumns(_invoices,
            ("Id", "Mã HĐ", 70),
            ("SessionId", "Mã phiên", 80),
            ("UsageCost", "Tiền giờ", 95),
            ("ServiceCost", "Dịch vụ", 95),
            ("Total", "Tổng tiền", 105),
            ("CreatedAt", "Thời điểm", 150),
            ("Status", "Trạng thái", 115));

        return CreateTab(
            "Hóa đơn",
            _invoices,
            ("Làm mới", () => LoadInvoicesAsync()),
            ("Lập từ mã phiên", CreateInvoiceAsync),
            ("Đã thanh toán", () => SetInvoiceStatusAsync(InvoiceStatuses.Paid)),
            ("Chưa thanh toán", () => SetInvoiceStatusAsync(InvoiceStatuses.Unpaid)));
    }

    private TabPage BuildMachineTab()
    {
        ConfigureColumns(_machines,
            ("Id", "Mã máy", 60),
            ("Name", "Tên máy", 120),
            ("IpAddress", "Địa chỉ IP", 120),
            ("Status", "Trạng thái", 100),
            ("LastHeartbeat", "Heartbeat cuối", 150),
            ("ActiveSession", "Đang có phiên", 90),
            ("Reason", "Lý do", 180));

        return CreateTab(
            "Máy trạm",
            _machines,
            ("Làm mới", () => LoadMachinesAsync()),
            ("Đổi trạng thái", SetMachineStatusAsync),
            ("Lịch sử trạng thái", ShowMachineHistoryAsync),
            ("Khóa máy", () => QueueRemoteCommandAsync(RemoteCommandTypes.LockComputer)),
            ("Đăng xuất máy", () => QueueRemoteCommandAsync(RemoteCommandTypes.LogoutComputer)),
            ("Lịch sử lệnh", ShowRemoteCommandHistoryAsync));
    }

    private TabPage BuildAuditTab()
    {
        ConfigureColumns(_audit,
            ("Id", "Mã", 55),
            ("Administrator", "Quản trị viên", 110),
            ("Action", "Hành động", 150),
            ("Target", "Đối tượng", 120),
            ("Succeeded", "Thành công", 75),
            ("Description", "Mô tả", 220),
            ("CreatedAt", "Thời điểm", 145));

        return CreateTab(
            "Nhật ký quản trị",
            _audit,
            ("Làm mới", () => LoadAuditAsync()));
    }

    private TabPage CreateTab(
        string title,
        DataGridView grid,
        params (string Text, Func<Task> Action)[] actions)
    {
        var page = new TabPage(title) { Padding = new Padding(8) };
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 7)
        };
        foreach ((string text, Func<Task> action) in actions)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 34,
                Margin = new Padding(0, 0, 7, 5)
            };
            button.Click += async (_, _) => await RunAsync(action);
            toolbar.Controls.Add(button);
        }

        page.Controls.Add(grid);
        page.Controls.Add(toolbar);
        return page;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            return;
        }

        try
        {
            Enabled = false;
            await action();
        }
        catch (Exception exception)
        {
            _setStatus(exception.Message, true);
            MessageBox.Show(
                FindForm(),
                exception.Message,
                "Thao tác quản trị thất bại",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            Enabled = true;
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task LoadCustomersAsync(CancellationToken cancellationToken = default)
    {
        List<CustomerSummary> items = await SendAsync<List<CustomerSummary>>(
            NetworkMessageTypes.AdminCustomerList,
            new { },
            cancellationToken);
        _customers.Rows.Clear();
        foreach (CustomerSummary item in items)
        {
            _customers.Rows.Add(
                item.Id,
                item.Username,
                item.FullName,
                item.Email,
                $"{item.Balance:N0} đ",
                item.IsActive ? "Có" : "Không");
            _customers.Rows[^1].Tag = item;
        }
        SelectFirst(_customers);
        _setStatus($"Đã tải {items.Count} tài khoản khách hàng.", false);
    }

    private async Task LoadPackagesAsync(CancellationToken cancellationToken = default)
    {
        List<HourPackageSummary> items = await SendAsync<List<HourPackageSummary>>(
            NetworkMessageTypes.AdminHourPackageList,
            new { },
            cancellationToken);
        _packageItems.Clear();
        _packageItems.AddRange(items);
        _packages.Rows.Clear();
        foreach (HourPackageSummary item in items)
        {
            _packages.Rows.Add(
                item.Id,
                item.Name,
                item.Hours,
                $"{item.Price:N0} đ",
                item.IsActive ? "Có" : "Không");
        }
        SelectFirst(_packages);
        _setStatus($"Đã tải {items.Count} gói giờ.", false);
    }

    private async Task LoadInventoryAsync(CancellationToken cancellationToken = default)
    {
        List<ServiceItemSummary> items = await SendAsync<List<ServiceItemSummary>>(
            NetworkMessageTypes.AdminInventoryList,
            new { },
            cancellationToken);
        _inventory.Rows.Clear();
        foreach (ServiceItemSummary item in items)
        {
            _inventory.Rows.Add(
                item.Id,
                item.Name,
                item.Type,
                item.Unit,
                $"{item.UnitPrice:N0} đ",
                item.StockQuantity,
                item.ReferenceStockQuantity,
                item.InventoryState);
            _inventory.Rows[^1].Tag = item;
        }
        SelectFirst(_inventory);
        _setStatus($"Đã tải {items.Count} mặt hàng/dịch vụ.", false);
    }

    private async Task LoadInvoicesAsync(CancellationToken cancellationToken = default)
    {
        List<InvoiceSummary> items = await SendAsync<List<InvoiceSummary>>(
            NetworkMessageTypes.AdminInvoiceList,
            new { },
            cancellationToken);
        _invoices.Rows.Clear();
        foreach (InvoiceSummary item in items)
        {
            _invoices.Rows.Add(
                item.Id,
                item.UsageSessionId,
                $"{item.UsageCost:N0} đ",
                $"{item.ServiceCost:N0} đ",
                $"{item.TotalAmount:N0} đ",
                item.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                item.Status);
            _invoices.Rows[^1].Tag = item;
        }
        SelectFirst(_invoices);
        _setStatus($"Đã tải {items.Count} hóa đơn gần nhất.", false);
    }

    private async Task CreateCustomerAsync()
    {
        using var dialog = new AdminInputDialog("Tạo tài khoản khách hàng");
        TextBox username = dialog.AddText("Tên đăng nhập");
        TextBox email = dialog.AddText("Email");
        TextBox fullName = dialog.AddText("Họ tên");
        TextBox password = dialog.AddText("Mật khẩu", isPassword: true);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        CustomerResult<CustomerSummary> result =
            await SendAsync<CustomerResult<CustomerSummary>>(
                NetworkMessageTypes.AdminCustomerCreate,
                new CreateCustomerRequest(
                    username.Text,
                    email.Text,
                    fullName.Text,
                    password.Text));
        EnsureSuccess(result.IsSuccess, result.Message);
        await LoadCustomersAsync();
    }

    private async Task UpdateCustomerAsync()
    {
        CustomerSummary selected = SelectedCustomer();
        using var dialog = new AdminInputDialog($"Cập nhật {selected.Username}");
        TextBox email = dialog.AddText("Email", selected.Email);
        TextBox fullName = dialog.AddText("Họ tên", selected.FullName ?? string.Empty);
        CheckBox active = dialog.AddCheck("Tài khoản đang hoạt động", selected.IsActive);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        CustomerResult<CustomerSummary> result =
            await SendAsync<CustomerResult<CustomerSummary>>(
                NetworkMessageTypes.AdminCustomerUpdate,
                new UpdateCustomerRequest(
                    selected.Id,
                    email.Text,
                    fullName.Text,
                    active.Checked));
        EnsureSuccess(result.IsSuccess, result.Message);
        await LoadCustomersAsync();
    }

    private async Task TopUpAsync()
    {
        CustomerSummary customer = SelectedCustomer();
        if (_packageItems.Count == 0)
        {
            await LoadPackagesAsync();
        }

        HourPackageSummary[] available = _packageItems
            .Where(item => item.IsActive)
            .ToArray();
        if (available.Length == 0)
        {
            throw new InvalidOperationException("Chưa có gói giờ đang áp dụng.");
        }

        using var dialog = new AdminInputDialog($"Nạp tiền cho {customer.Username}");
        ComboBox package = dialog.AddCombo(
            "Gói giờ",
            available,
            item => $"{item.Name} - {item.Hours} giờ - {item.Price:N0} đ");
        TextBox note = dialog.AddText("Ghi chú");
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        HourPackageSummary selected = (HourPackageSummary)package.SelectedItem!;
        BillingResult<TopUpReceipt> result =
            await SendAsync<BillingResult<TopUpReceipt>>(
                NetworkMessageTypes.AdminTopUp,
                new TopUpRequest(
                    customer.Id,
                    selected.Id,
                    note.Text,
                    Guid.NewGuid()));
        EnsureSuccess(result.IsSuccess, result.Message);
        await LoadCustomersAsync();
    }

    private async Task RefundAsync()
    {
        CustomerSummary customer = SelectedCustomer();
        using var dialog = new AdminInputDialog(
            $"Hoàn toàn bộ {customer.Balance:N0} đ và đóng {customer.Username}");
        ComboBox method = dialog.AddCombo(
            "Phương thức",
            new[] { RefundMethods.Cash, RefundMethods.BankTransfer },
            value => value == RefundMethods.Cash ? "Tiền mặt" : "Chuyển khoản");
        TextBox reason = dialog.AddText("Lý do");
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        BillingResult<BalanceRefundReceipt> result =
            await SendAsync<BillingResult<BalanceRefundReceipt>>(
                NetworkMessageTypes.AdminRefundCreate,
                new BalanceRefundRequest(
                    customer.Id,
                    (string)method.SelectedItem!,
                    reason.Text,
                    Guid.NewGuid()));
        EnsureSuccess(result.IsSuccess, result.Message);
        await LoadCustomersAsync();
    }

    private async Task ShowCustomerHistoryAsync()
    {
        CustomerSummary customer = SelectedCustomer();
        List<TopUpHistoryItem> topUps = await SendAsync<List<TopUpHistoryItem>>(
            NetworkMessageTypes.AdminTopUpHistory,
            new CustomerIdRequest(customer.Id));
        List<BalanceRefundHistoryItem> refunds =
            await SendAsync<List<BalanceRefundHistoryItem>>(
                NetworkMessageTypes.AdminRefundHistory,
                new CustomerIdRequest(customer.Id));
        using var history = new AdminHistoryDialog(customer.Username, topUps, refunds);
        history.ShowDialog(FindForm());
    }

    private async Task SavePackageAsync(HourPackageSummary? existing)
    {
        using var dialog = new AdminInputDialog(
            existing is null ? "Thêm gói giờ" : "Sửa gói giờ");
        TextBox name = dialog.AddText("Tên gói", existing?.Name ?? string.Empty);
        NumericUpDown hours = dialog.AddNumber("Số giờ", existing?.Hours ?? 1, 1, 10000, 0);
        NumericUpDown price = dialog.AddNumber("Giá tiền", existing?.Price ?? 10000m, 1, 9_999_999_999m, 0);
        CheckBox active = dialog.AddCheck("Đang áp dụng", existing?.IsActive ?? true);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        BillingResult<HourPackageSummary> result =
            await SendAsync<BillingResult<HourPackageSummary>>(
                NetworkMessageTypes.AdminHourPackageSave,
                new SaveHourPackageRequest(
                    existing?.Id,
                    name.Text,
                    (int)hours.Value,
                    price.Value,
                    active.Checked));
        EnsureSuccess(result.IsSuccess, result.Message);
        await LoadPackagesAsync();
    }

    private async Task SaveInventoryAsync(ServiceItemSummary? existing)
    {
        using var dialog = new AdminInputDialog(
            existing is null ? "Thêm mặt hàng / dịch vụ" : "Sửa mặt hàng / dịch vụ");
        TextBox name = dialog.AddText("Tên", existing?.Name ?? string.Empty);
        ComboBox type = dialog.AddCombo(
            "Loại",
            new[] { ServiceItemTypes.Product, ServiceItemTypes.Overnight },
            value => value == ServiceItemTypes.Product ? "Sản phẩm" : "Ngủ qua đêm");
        type.SelectedItem = existing?.Type ?? ServiceItemTypes.Product;
        TextBox unit = dialog.AddText("Đơn vị", existing?.Unit ?? string.Empty);
        NumericUpDown price = dialog.AddNumber(
            "Đơn giá", existing?.UnitPrice ?? 10000m, 0, 9_999_999_999m, 0);
        NumericUpDown stock = dialog.AddNumber(
            "Số lượng tồn", existing?.StockQuantity ?? 0, 0, 1_000_000, 0);
        NumericUpDown reference = dialog.AddNumber(
            "Mức tồn chuẩn", existing?.ReferenceStockQuantity ?? 10, 1, 1_000_000, 0);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        string selectedType = (string)type.SelectedItem!;
        InventoryResult<ServiceItemSummary> result =
            await SendAsync<InventoryResult<ServiceItemSummary>>(
                NetworkMessageTypes.AdminInventorySave,
                new SaveServiceItemRequest(
                    existing?.Id,
                    name.Text,
                    selectedType,
                    unit.Text,
                    price.Value,
                    selectedType == ServiceItemTypes.Product ? (int)stock.Value : null,
                    selectedType == ServiceItemTypes.Product ? (int)reference.Value : null));
        EnsureSuccess(result.IsSuccess, result.Message);
        await LoadInventoryAsync();
    }

    private async Task AdjustInventoryAsync()
    {
        ServiceItemSummary item = SelectedInventoryItem();
        if (item.Type != ServiceItemTypes.Product)
        {
            throw new InvalidOperationException("Chỉ sản phẩm mới có số lượng tồn kho.");
        }

        using var dialog = new AdminInputDialog($"Nhập / xuất kho: {item.Name}");
        NumericUpDown quantity = dialog.AddNumber(
            "Số lượng thay đổi (âm là xuất kho)", 1, -1_000_000, 1_000_000, 0);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        InventoryResult<ServiceItemSummary> result =
            await SendAsync<InventoryResult<ServiceItemSummary>>(
                NetworkMessageTypes.AdminInventoryAdjust,
                new AdjustStockRequest(item.Id, (int)quantity.Value));
        EnsureSuccess(result.IsSuccess, result.Message);
        await LoadInventoryAsync();
    }

    private async Task DeleteInventoryAsync()
    {
        ServiceItemSummary item = SelectedInventoryItem();
        DialogResult answer = MessageBox.Show(
            FindForm(),
            $"Xóa '{item.Name}'? Sản phẩm đã phát sinh giao dịch sẽ được Server từ chối xóa.",
            "Xác nhận xóa",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        InventoryResult<bool> result = await SendAsync<InventoryResult<bool>>(
            NetworkMessageTypes.AdminInventoryDelete,
            new ServiceItemIdRequest(item.Id));
        EnsureSuccess(result.IsSuccess, result.Message);
        await LoadInventoryAsync();
    }

    private async Task CreateInvoiceAsync()
    {
        using var dialog = new AdminInputDialog("Lập hóa đơn từ phiên đã kết thúc");
        NumericUpDown sessionId = dialog.AddNumber(
            "Mã phiên sử dụng", 1, 1, 9_999_999_999m, 0);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        InvoiceResult<InvoiceSummary> result =
            await SendAsync<InvoiceResult<InvoiceSummary>>(
                NetworkMessageTypes.AdminInvoiceCreate,
                new CreateInvoiceRequest((long)sessionId.Value, 0m));
        EnsureSuccess(result.IsSuccess, result.Message);
        await LoadInvoicesAsync();
    }

    private async Task SetInvoiceStatusAsync(string status)
    {
        InvoiceSummary invoice = SelectedInvoice();
        InvoiceResult<InvoiceSummary> result =
            await SendAsync<InvoiceResult<InvoiceSummary>>(
                NetworkMessageTypes.AdminInvoiceSetStatus,
                new InvoiceStatusRequest(invoice.Id, status));
        EnsureSuccess(result.IsSuccess, result.Message);
        await LoadInvoicesAsync();
    }

    private async Task LoadMachinesAsync(
        CancellationToken cancellationToken = default)
    {
        List<MachineAdminSummary> items =
            await SendAsync<List<MachineAdminSummary>>(
                NetworkMessageTypes.AdminMachineList,
                new { },
                cancellationToken);
        _machines.Rows.Clear();
        foreach (MachineAdminSummary item in items)
        {
            int index = _machines.Rows.Add(
                item.Id,
                item.Name,
                item.IpAddress ?? string.Empty,
                item.Status,
                item.LastHeartbeatAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
                    ?? "Chưa có",
                item.HasActiveSession ? "Có" : "Không",
                item.MaintenanceReason ?? string.Empty);
            DataGridViewRow row = _machines.Rows[index];
            row.Tag = item;
            row.DefaultCellStyle.BackColor = item.Status switch
            {
                ComputerStatuses.Available => Color.Honeydew,
                ComputerStatuses.InUse => Color.LightCyan,
                ComputerStatuses.Maintenance => Color.LemonChiffon,
                ComputerStatuses.Incident => Color.MistyRose,
                ComputerStatuses.Disconnected => Color.Gainsboro,
                ComputerStatuses.Locked => Color.LightGray,
                _ => Color.White
            };
        }
        SelectFirst(_machines);
    }

    private async Task LoadAuditAsync(
        CancellationToken cancellationToken = default)
    {
        List<AdministrativeAuditSummary> items =
            await SendAsync<List<AdministrativeAuditSummary>>(
                NetworkMessageTypes.AdminAuditList,
                new { },
                cancellationToken);
        _audit.Rows.Clear();
        foreach (AdministrativeAuditSummary item in items)
        {
            int index = _audit.Rows.Add(
                item.Id,
                item.AdministratorUsername,
                item.Action,
                $"{item.TargetType}: {item.TargetId ?? "-"}",
                item.Succeeded ? "Có" : "Không",
                item.Description,
                item.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"));
            _audit.Rows[index].Tag = item;
            _audit.Rows[index].DefaultCellStyle.BackColor =
                item.Succeeded ? Color.Honeydew : Color.MistyRose;
        }
        SelectFirst(_audit);
    }

    private async Task SetMachineStatusAsync()
    {
        MachineAdminSummary machine = SelectedMachine();
        using var dialog = new AdminInputDialog($"Đổi trạng thái {machine.Name}");
        string[] statuses =
        [
            ComputerStatuses.Available,
            ComputerStatuses.Locked,
            ComputerStatuses.Maintenance,
            ComputerStatuses.Incident
        ];
        ComboBox status = dialog.AddCombo(
            "Trạng thái",
            statuses,
            value => value);
        status.SelectedItem = statuses.Contains(machine.Status)
            ? machine.Status
            : ComputerStatuses.Available;
        TextBox reason = dialog.AddText(
            "Lý do",
            machine.MaintenanceReason ?? string.Empty);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        AdminResult<MachineAdminSummary> result =
            await SendAsync<AdminResult<MachineAdminSummary>>(
                NetworkMessageTypes.AdminMachineSetStatus,
                new SetMachineStatusRequest(
                    machine.Id,
                    status.SelectedItem?.ToString(),
                    reason.Text));
        EnsureSuccess(result.IsSuccess, result.Message);
        await LoadMachinesAsync();
    }

    private async Task ShowMachineHistoryAsync()
    {
        MachineAdminSummary machine = SelectedMachine();
        List<MachineStatusHistorySummary> history =
            await SendAsync<List<MachineStatusHistorySummary>>(
                NetworkMessageTypes.AdminMachineHistoryList,
                new MachineStatusHistoryRequest(machine.Id));
        string text = string.Join(
            Environment.NewLine,
            history.Select(item =>
                $"{item.ChangedAt.ToLocalTime():dd/MM/yyyy HH:mm} | " +
                $"{item.PreviousStatus} -> {item.NewStatus} | " +
                $"{item.AdministratorUsername} | {item.Reason ?? "Không có lý do"}"));
        MessageBox.Show(
            FindForm(),
            text.Length == 0 ? "Chưa có lịch sử thay đổi." : text,
            $"Lịch sử trạng thái - {machine.Name}",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task QueueRemoteCommandAsync(string commandType)
    {
        MachineAdminSummary machine = SelectedMachine();
        string action = commandType == RemoteCommandTypes.LockComputer
            ? "khóa"
            : "đăng xuất tài khoản trên";
        if (MessageBox.Show(
                FindForm(),
                $"Gửi lệnh {action} máy {machine.Name}?",
                "Xác nhận lệnh điều khiển",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        AdminResult<RemoteCommandSummary> result =
            await SendAsync<AdminResult<RemoteCommandSummary>>(
                NetworkMessageTypes.AdminRemoteCommandQueue,
                new QueueRemoteCommandRequest(machine.Id, commandType));
        EnsureSuccess(result.IsSuccess, result.Message);
        _setStatus(result.Message, false);
        await LoadAuditAsync();
    }

    private async Task ShowRemoteCommandHistoryAsync()
    {
        MachineAdminSummary machine = SelectedMachine();
        List<RemoteCommandSummary> history =
            await SendAsync<List<RemoteCommandSummary>>(
                NetworkMessageTypes.AdminRemoteCommandHistory,
                new RemoteCommandHistoryRequest(machine.Id));
        string text = string.Join(
            Environment.NewLine,
            history.Select(item =>
                $"{item.SentAt.ToLocalTime():dd/MM/yyyy HH:mm} | " +
                $"{item.CommandType} | {item.Status} | " +
                $"{item.AdministratorUsername}" +
                (item.ErrorCode is null ? string.Empty : $" | Lỗi: {item.ErrorCode}")));
        MessageBox.Show(
            FindForm(),
            text.Length == 0 ? "Chưa có lệnh điều khiển." : text,
            $"Lịch sử lệnh - {machine.Name}",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task<T> SendAsync<T>(
        string type,
        object payload,
        CancellationToken cancellationToken = default)
        where T : class
    {
        JsonElement data = await _send(type, payload, cancellationToken);
        return data.Deserialize<T>(JsonOptions)
            ?? throw new InvalidDataException(
                $"Server không trả dữ liệu {typeof(T).Name} hợp lệ.");
    }

    private CustomerSummary SelectedCustomer()
    {
        DataGridViewRow row = SelectedRow(_customers, "Hãy chọn khách hàng.");
        return row.Tag as CustomerSummary
            ?? throw new InvalidOperationException("Không đọc được khách hàng đã chọn.");
    }

    private HourPackageSummary SelectedPackage()
    {
        DataGridViewRow row = SelectedRow(_packages, "Hãy chọn gói giờ.");
        int id = Convert.ToInt32(row.Cells["Id"].Value);
        return _packageItems.Single(item => item.Id == id);
    }

    private ServiceItemSummary SelectedInventoryItem()
    {
        DataGridViewRow row = SelectedRow(_inventory, "Hãy chọn mặt hàng/dịch vụ.");
        return row.Tag as ServiceItemSummary
            ?? throw new InvalidOperationException("Không đọc được mặt hàng đã chọn.");
    }

    private InvoiceSummary SelectedInvoice()
    {
        DataGridViewRow row = SelectedRow(_invoices, "Hãy chọn hóa đơn.");
        return row.Tag as InvoiceSummary
            ?? throw new InvalidOperationException("Không đọc được hóa đơn đã chọn.");
    }

    private MachineAdminSummary SelectedMachine()
    {
        DataGridViewRow row = SelectedRow(_machines, "Hãy chọn máy trạm.");
        return row.Tag as MachineAdminSummary
            ?? throw new InvalidOperationException("Không đọc được máy trạm đã chọn.");
    }

    private static DataGridViewRow SelectedRow(DataGridView grid, string message) =>
        grid.CurrentRow ?? throw new InvalidOperationException(message);

    private static void EnsureSuccess(bool isSuccess, string message)
    {
        if (!isSuccess)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static DataGridView CreateGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = false,
        BackgroundColor = Color.White
    };

    private static void ConfigureColumns(
        DataGridView grid,
        params (string Name, string Header, int Width)[] columns)
    {
        foreach ((string name, string header, int width) in columns)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                MinimumWidth = Math.Min(width, 80),
                FillWeight = width
            });
        }
    }

    private static void SelectFirst(DataGridView grid)
    {
        if (grid.Rows.Count > 0)
        {
            grid.Rows[0].Selected = true;
            grid.CurrentCell = grid.Rows[0].Cells[0];
        }
    }
}

internal sealed class AdminInputDialog : Form
{
    private readonly TableLayoutPanel _fields = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        ColumnCount = 1,
        Padding = new Padding(18)
    };

    public AdminInputDialog(string title)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(470, 500);
        MinimizeBox = false;
        MaximizeBox = false;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        var save = new Button
        {
            Text = "Lưu",
            DialogResult = DialogResult.OK,
            AutoSize = true
        };
        var cancel = new Button
        {
            Text = "Hủy",
            DialogResult = DialogResult.Cancel,
            AutoSize = true
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        Controls.Add(_fields);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
    }

    public TextBox AddText(
        string label,
        string value = "",
        bool isPassword = false)
    {
        var input = new TextBox
        {
            Text = value,
            UseSystemPasswordChar = isPassword,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 10.5f)
        };
        AddField(label, input);
        return input;
    }

    public NumericUpDown AddNumber(
        string label,
        decimal value,
        decimal minimum,
        decimal maximum,
        int decimalPlaces)
    {
        var input = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(value, minimum, maximum),
            DecimalPlaces = decimalPlaces,
            ThousandsSeparator = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 10.5f)
        };
        AddField(label, input);
        return input;
    }

    public CheckBox AddCheck(string label, bool value)
    {
        var input = new CheckBox
        {
            Text = label,
            Checked = value,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 12)
        };
        AddControl(input);
        return input;
    }

    public ComboBox AddCombo<T>(
        string label,
        IEnumerable<T> values,
        Func<T, string> display)
        where T : notnull
    {
        var input = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 10.5f)
        };
        foreach (T value in values)
        {
            input.Items.Add(value);
        }
        input.Format += (_, eventArgs) =>
        {
            if (eventArgs.ListItem is T item)
            {
                eventArgs.Value = display(item);
            }
        };
        if (input.Items.Count > 0) input.SelectedIndex = 0;
        AddField(label, input);
        return input;
    }

    private void AddField(string label, Control input)
    {
        AddControl(new Label
        {
            Text = label,
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Margin = new Padding(0, 4, 0, 3)
        });
        input.Margin = new Padding(0, 0, 0, 12);
        AddControl(input);
    }

    private void AddControl(Control control)
    {
        int row = _fields.RowCount++;
        _fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _fields.Controls.Add(control, 0, row);
    }
}

internal sealed class AdminHistoryDialog : Form
{
    public AdminHistoryDialog(
        string username,
        IReadOnlyList<TopUpHistoryItem> topUps,
        IReadOnlyList<BalanceRefundHistoryItem> refunds)
    {
        Text = $"Lịch sử giao dịch - {username}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 520);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreatePage(
            "Nạp tiền",
            topUps.Select(item => new object[]
            {
                item.TransactionId,
                item.PackageName,
                item.HoursAdded,
                $"{item.Amount:N0} đ",
                item.PerformedBy,
                item.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                item.Note ?? string.Empty
            }),
            "Mã", "Gói", "Giờ", "Số tiền", "Người thực hiện", "Thời điểm", "Ghi chú"));
        tabs.TabPages.Add(CreatePage(
            "Hoàn tiền",
            refunds.Select(item => new object[]
            {
                item.TransactionId,
                $"{item.Amount:N0} đ",
                item.Method,
                item.Reason,
                item.PerformedBy,
                item.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
            }),
            "Mã", "Số tiền", "Phương thức", "Lý do", "Người thực hiện", "Thời điểm"));
        Controls.Add(tabs);
    }

    private static TabPage CreatePage(
        string title,
        IEnumerable<object[]> rows,
        params string[] columns)
    {
        var page = new TabPage(title) { Padding = new Padding(8) };
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false
        };
        foreach (string column in columns)
        {
            grid.Columns.Add(column, column);
        }
        foreach (object[] row in rows)
        {
            grid.Rows.Add(row);
        }
        page.Controls.Add(grid);
        return page;
    }
}
