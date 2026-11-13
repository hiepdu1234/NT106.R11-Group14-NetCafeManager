using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;
using NetCafeManager.Client.Networking;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Networking;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Client;

/// <summary>
/// Giao diện desktop dùng chung cho máy khách và máy quản trị.
/// Server xác thực tài khoản và trả vai trò; ứng dụng chỉ định tuyến
/// đến giao diện tương ứng, không cho người dùng tự chọn quyền.
/// </summary>
public sealed class MainForm : Form
{
    // Bang mau thong nhat theo Muc 1 cua tai lieu thiet ke.
    // Khi tach thanh nhieu Form/UserControl, nen chuyen sang AppTheme dung chung.
    private static class Theme
    {
        public static readonly Color Navy = ColorTranslator.FromHtml("#0F172A");
        public static readonly Color Primary = ColorTranslator.FromHtml("#2563EB");
        public static readonly Color Cyan = ColorTranslator.FromHtml("#0891B2");
        public static readonly Color Background = ColorTranslator.FromHtml("#F8FAFC");
        public static readonly Color Surface = Color.White;
        public static readonly Color TextSecondary = ColorTranslator.FromHtml("#64748B");
        public static readonly Color Border = ColorTranslator.FromHtml("#E2E8F0");
        public static readonly Color LightBlue = ColorTranslator.FromHtml("#DBEAFE");
        public static readonly Color Error = ColorTranslator.FromHtml("#B91C1C");
        public static readonly Color Success = ColorTranslator.FromHtml("#15803D");
    }

    private enum RecoveryStage { None, OtpRequested, OtpVerified }

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, Control> _pages = new(StringComparer.Ordinal);
    private readonly List<Button> _buttons = new();
    private readonly Panel _pageHost = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Theme.Surface,
        Margin = Padding.Empty
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true,
        AccessibleName = "Thông báo trạng thái",
        Text = "Hãy thiết lập thiết bị và kết nối tới Server."
    };
    private readonly Label _deviceBadge = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Theme.Cyan,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        AccessibleName = "Thông tin thiết bị"
    };

    private DesktopSettings _settings = DesktopSettings.Load();
    private readonly TextBox _serverHost = NewTextBox("Địa chỉ Server");
    private readonly TextBox _serverPort = NewTextBox("Cổng Server");
    private readonly ComboBox _deviceMode = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        AccessibleName = "Loại thiết bị",
        Font = new Font("Segoe UI", 11),
        Margin = new Padding(0, 5, 0, 13)
    };
    private readonly TextBox _machineName = NewTextBox("Tên máy");
    private readonly TextBox _machineKey = NewTextBox("Khóa máy Base64", isPassword: true);
    private readonly Label _machineStatus = Hint("Máy chưa được xác thực.");
    private readonly ComboBox _recoveryRole = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        AccessibleName = "Vai trò tài khoản khôi phục",
        Font = new Font("Segoe UI", 11),
        Margin = new Padding(0, 5, 0, 13)
    };

    private readonly TextBox _loginUsername = NewTextBox("Tên đăng nhập");
    private readonly TextBox _loginPassword = NewTextBox("Mật khẩu", isPassword: true);
    private readonly TextBox _identifier = NewTextBox("Tên đăng nhập hoặc email đã đăng ký");
    private readonly TextBox _otp = NewTextBox("Mã OTP gồm 6 chữ số");
    private readonly TextBox _newPassword = NewTextBox("Mật khẩu mới", isPassword: true);
    private readonly TextBox _confirmPassword = NewTextBox("Xác nhận mật khẩu mới", isPassword: true);
    private readonly Label _clientWelcome = Hint("");
    private readonly Label _clientSession = Hint("");
    private readonly Label _adminWelcome = Hint("");

    private readonly ErrorProvider _errors = new();

    private Button? _handshakeButton;
    private Button? _machineLink;
    private TcpClientAgent? _client;
    private UsageSessionSummary? _activeSession;
    private bool _machineVerified;
    private bool _authenticated;
    private Button? _loginButton;
    private Button? _requestOtpButton;
    private Button? _verifyOtpButton;
    private Button? _resetButton;
    private Button? _clientLogoutButton;
    private Button? _adminLogoutButton;
    private CheckBox? _showLoginPassword;
    private CheckBox? _showNewPasswords;
    private bool _busy;
    private RecoveryStage _recoveryStage;
    private string _currentPage = "";

    public MainForm()
    {
        Text = "NetCafeManager | Đăng nhập";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        MinimumSize = new Size(840, 660);
        ClientSize = new Size(1000, 710);
        Font = new Font("Segoe UI", 10);
        BackColor = Theme.Background;

        _errors.ContainerControl = this;
        _errors.BlinkStyle = ErrorBlinkStyle.NeverBlink;
        FormClosed += (_, _) =>
        {
            ResetConnection();
            _errors.Dispose();
        };

        _serverHost.Text = _settings.ServerHost;
        _serverPort.Text = _settings.ServerPort.ToString();
        _machineName.Text = _settings.MachineName;
        _machineKey.Text = _settings.TryUnprotectMachineKey() ?? string.Empty;
        _deviceMode.Items.AddRange([
            WorkstationMode.Client,
            WorkstationMode.Administration
        ]);
        _deviceMode.SelectedItem = _settings.Mode;
        _deviceMode.Format += (_, e) =>
        {
            e.Value = e.ListItem is WorkstationMode.Administration
                ? "Máy quản trị"
                : "Máy khách";
        };
        _recoveryRole.Items.AddRange([UserRole.Client, UserRole.Administrator]);
        _recoveryRole.SelectedItem = UserRole.Client;
        _recoveryRole.SelectedIndexChanged += (_, _) => InvalidateRecovery();
        _machineName.TextChanged += (_, _) =>
        {
            UpdateDeviceBadge();
            if (_machineVerified || _authenticated)
            {
                ResetConnection();
                SetStatus("Tên máy đã đổi. Hãy xác thực máy lại.");
            }
        };
        _serverHost.TextChanged += (_, _) => InvalidateConnectionSettings();
        _serverPort.TextChanged += (_, _) => InvalidateConnectionSettings();
        _deviceMode.SelectedIndexChanged += (_, _) =>
        {
            UpdateDeviceBadge();
            if (_authenticated)
            {
                ResetConnection();
                ShowPage("login");
            }
        };

        _otp.MaxLength = 6;
        _newPassword.MaxLength = 128;
        _confirmPassword.MaxLength = 128;
        _loginPassword.MaxLength = 128;
        _machineName.MaxLength = 50;
        _machineKey.MaxLength = 512;
        _serverHost.MaxLength = 253;
        _serverPort.MaxLength = 5;

        // Neu nguoi dung sua tai khoan trong khi dang khoi phuc,
        // phai xac minh OTP lai; khong giu trang thai cu.
        _identifier.TextChanged += (_, _) => InvalidateRecovery();

        BuildLayout();
        BuildPages();
        UpdateDeviceBadge();
        ShowPage("login");
        Shown += (_, _) => _loginUsername.Focus();
    }

    private static TextBox NewTextBox(string name, bool isPassword = false) => new()
    {
        Name = name.Replace(' ', '_'),
        AccessibleName = name,
        PlaceholderText = name,
        Font = new Font("Segoe UI", 11),
        BorderStyle = BorderStyle.FixedSingle,
        UseSystemPasswordChar = isPassword,
        BackColor = Theme.Surface,
        ForeColor = Theme.Navy,
        Dock = DockStyle.Top,
        Margin = new Padding(0, 5, 0, 13)
    };

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Navy,
            Padding = new Padding(30, 10, 30, 10),
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

        var brand = new Label
        {
            Text = "NETCAFE MANAGER",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI", 19, FontStyle.Bold),
            ForeColor = Theme.Surface,
            AccessibleName = "NetCafe Manager"
        };
        var subtitle = new Label
        {
            Text = "Đăng nhập chung cho khách hàng và quản trị viên",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(203, 213, 225),
            Font = new Font("Segoe UI", 10)
        };
        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(subtitle, 0, 1);
        header.Controls.Add(_deviceBadge, 1, 0);
        header.SetRowSpan(_deviceBadge, 2);
        root.Controls.Add(header, 0, 0);

        var center = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(0, 20, 0, 20),
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty
        };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _pageHost.Paint += (_, e) =>
        {
            using var border = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(
                border, 0, 0, _pageHost.ClientSize.Width - 1, _pageHost.ClientSize.Height - 1);
        };
        center.Controls.Add(_pageHost, 1, 0);
        root.Controls.Add(center, 0, 1);

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(24, 5, 24, 5),
            Margin = Padding.Empty
        };
        _status.Font = new Font("Segoe UI", 9);
        _status.ForeColor = Theme.TextSecondary;
        footer.Controls.Add(_status);
        root.Controls.Add(footer, 0, 2);
    }

    private void BuildPages()
    {
        // Thiết lập loại thiết bị chỉ quyết định trải nghiệm cục bộ.
        // Vai trò và quyền luôn do Server trả về sau khi xác thực tài khoản.
        TableLayoutPanel machine = NewPage(
            "THIẾT LẬP THIẾT BỊ", "Cấu hình kết nối và xác thực thiết bị với Server.");
        AddField(machine, "Loại thiết bị", _deviceMode);
        AddField(machine, "Địa chỉ Server", _serverHost);
        AddField(machine, "Cổng Server", _serverPort);
        AddField(machine, "Tên máy", _machineName);
        AddField(machine, "Khóa máy Base64", _machineKey);
        AddItem(machine, Hint(
            "Khóa máy được mã hóa bằng Windows DPAPI trước khi lưu trên thiết bị này."));
        _handshakeButton = NewButton("Lưu cấu hình và xác thực thiết bị", isPrimary: true);
        _handshakeButton.Click += async (_, _) => await ExecuteAsync(HandshakeAsync);
        AddItem(machine, _handshakeButton);
        AddItem(machine, _machineStatus);
        AddBackButton(machine, "login");
        RegisterPage("machine", machine);

        // Màn hình đăng nhập chung. Người dùng không tự chọn vai trò.
        TableLayoutPanel login = NewPage(
            "ĐĂNG NHẬP", "Server sẽ xác định vai trò và mở giao diện phù hợp.");
        _machineLink = NewButton("Thiết lập / xác thực thiết bị");
        _machineLink.Click += (_, _) => ShowPage("machine");
        AddItem(login, _machineLink);
        AddItem(login, Hint("Thiết bị phải được xác thực trước khi đăng nhập."));
        AddField(login, "Tên đăng nhập", _loginUsername);
        AddField(login, "Mật khẩu", _loginPassword);
        _showLoginPassword = NewCheckBox("Hiện mật khẩu");
        _showLoginPassword.CheckedChanged += (_, _) =>
            _loginPassword.UseSystemPasswordChar = !_showLoginPassword.Checked;
        AddItem(login, _showLoginPassword);
        _loginButton = NewButton("Đăng nhập", isPrimary: true);
        _loginButton.Click += async (_, _) => await ExecuteAsync(LoginAsync);
        AddItem(login, _loginButton);
        Button forgotLink = NewButton("Quên mật khẩu?");
        forgotLink.Click += (_, _) => ShowPage("forgot");
        AddItem(login, forgotLink);
        RegisterPage("login", login);

        TableLayoutPanel clientDashboard = NewPage(
            "GIAO DIỆN KHÁCH HÀNG", "Phiên sử dụng được tính và lưu trên Server.");
        AddItem(clientDashboard, _clientWelcome);
        AddItem(clientDashboard, _clientSession);
        AddItem(clientDashboard, Hint(
            "Các chức năng số dư, gói giờ, lịch sử và dịch vụ sẽ được tích hợp vào khu vực này."));
        _clientLogoutButton = NewButton("Kết thúc phiên và đăng xuất", isPrimary: true);
        _clientLogoutButton.Click += async (_, _) => await ExecuteAsync(EndSessionAsync);
        AddItem(clientDashboard, _clientLogoutButton);
        RegisterPage("client-dashboard", clientDashboard);

        TableLayoutPanel adminDashboard = NewPage(
            "QUẢN TRỊ MÁY CHỦ", "Mọi thao tác quản trị vẫn được Server kiểm tra quyền.");
        AddItem(adminDashboard, _adminWelcome);
        AddItem(adminDashboard, Hint(
            "Khu vực quản lý tài khoản, máy trạm, số dư, hóa đơn và tồn kho sẽ được tích hợp tại đây."));
        _adminLogoutButton = NewButton("Đăng xuất", isPrimary: true);
        _adminLogoutButton.Click += async (_, _) => await ExecuteAsync(EndSessionAsync);
        AddItem(adminDashboard, _adminLogoutButton);
        RegisterPage("admin-dashboard", adminDashboard);

        // Man hinh 2: Yeu cau OTP.
        TableLayoutPanel forgot = NewPage(
            "QUÊN MẬT KHẨU", "Nhập thông tin tài khoản để yêu cầu mã OTP.");
        AddField(forgot, "Tên đăng nhập hoặc email", _identifier);
        AddField(forgot, "Vai trò tài khoản", _recoveryRole);
        AddItem(forgot, Hint("Mã xác nhận sẽ được gửi về email đăng ký nếu thông tin hợp lệ."));
        _requestOtpButton = NewButton("Yêu cầu mã OTP", isPrimary: true);
        _requestOtpButton.Click += async (_, _) => await ExecuteAsync(RequestOtpAsync);
        AddItem(forgot, _requestOtpButton);
        Button previewOtp = NewButton("Xem trước giao diện nhập OTP");
        previewOtp.Click += (_, _) =>
        {
            ShowPage("otp");
            SetStatus("Chế độ xem trước: muốn xác minh OTP cần yêu cầu mã trước.");
        };
        AddItem(forgot, previewOtp);
        AddBackButton(forgot, "login");
        RegisterPage("forgot", forgot);

        // Man hinh 3: Xac minh OTP.
        TableLayoutPanel otpPage = NewPage(
            "NHẬP MÃ OTP", "Mã xác nhận gồm đúng 6 chữ số.");
        AddItem(otpPage, Hint("Hãy yêu cầu OTP từ màn hình trước và kiểm tra email đăng ký."));
        AddField(otpPage, "Mã OTP", _otp);
        _verifyOtpButton = NewButton("Xác minh OTP", isPrimary: true);
        _verifyOtpButton.Click += async (_, _) => await ExecuteAsync(VerifyOtpAsync);
        AddItem(otpPage, _verifyOtpButton);
        Button previewReset = NewButton("Xem trước giao diện đặt mật khẩu mới");
        previewReset.Click += (_, _) =>
        {
            ShowPage("reset");
            SetStatus("Chỉ xem giao diện. Phải xác minh OTP thành công mới được đặt lại mật khẩu.");
        };
        AddItem(otpPage, previewReset);
        AddBackButton(otpPage, "forgot");
        RegisterPage("otp", otpPage);

        // Man hinh 4: Dat lai mat khau.
        TableLayoutPanel reset = NewPage(
            "ĐẶT MẬT KHẨU MỚI", "Mật khẩu mới phải có từ 8 đến 128 ký tự.");
        AddItem(reset, Hint("Chỉ được gửi yêu cầu sau khi Server xác minh OTP."));
        AddField(reset, "Mật khẩu mới", _newPassword);
        AddField(reset, "Nhập lại mật khẩu mới", _confirmPassword);
        _showNewPasswords = NewCheckBox("Hiện mật khẩu");
        _showNewPasswords.CheckedChanged += (_, _) =>
        {
            bool hide = !_showNewPasswords.Checked;
            _newPassword.UseSystemPasswordChar = hide;
            _confirmPassword.UseSystemPasswordChar = hide;
        };
        AddItem(reset, _showNewPasswords);
        _resetButton = NewButton("Đặt lại mật khẩu", isPrimary: true);
        _resetButton.Click += async (_, _) => await ExecuteAsync(ResetPasswordAsync);
        AddItem(reset, _resetButton);
        AddBackButton(reset, "otp");
        RegisterPage("reset", reset);
    }

    private static TableLayoutPanel NewPage(string title, string description)
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Surface,
            Padding = new Padding(32, 25, 32, 25),
            ColumnCount = 1,
            RowCount = 0,
            Margin = Padding.Empty
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddItem(page, new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = Theme.Navy,
            Margin = new Padding(0, 0, 0, 10)
        });
        AddItem(page, new Label
        {
            Text = description,
            AutoSize = true,
            Font = new Font("Segoe UI", 10),
            ForeColor = Theme.TextSecondary,
            Margin = new Padding(0, 0, 0, 23)
        });
        return page;
    }

    private static CheckBox NewCheckBox(string name) => new()
    {
        Text = name,
        AccessibleName = name,
        AutoSize = true,
        ForeColor = Theme.TextSecondary,
        Font = new Font("Segoe UI", 9),
        Margin = new Padding(0, 0, 0, 12)
    };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(470, 0),
        ForeColor = Theme.TextSecondary,
        Font = new Font("Segoe UI", 9),
        Margin = new Padding(0, 3, 0, 13)
    };

    private static void AddField(TableLayoutPanel page, string label, Control field)
    {
        AddItem(page, new Label
        {
            Text = label,
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Theme.Navy,
            Margin = new Padding(0, 3, 0, 2)
        });
        AddItem(page, field);
    }

    private static void AddItem(TableLayoutPanel page, Control control)
    {
        int row = page.RowCount++;
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Top;
        page.Controls.Add(control, 0, row);
    }

    private Button NewButton(string text, bool isPrimary = false)
    {
        var button = new Button
        {
            Text = text,
            AccessibleName = text,
            Height = 43,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Margin = new Padding(0, 7, 0, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = isPrimary ? Theme.Primary : Theme.Background,
            ForeColor = isPrimary ? Theme.Surface : Theme.Navy,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = isPrimary ? 0 : 1;
        button.FlatAppearance.BorderColor = isPrimary ? Theme.Primary : Theme.Border;
        button.FlatAppearance.MouseOverBackColor = isPrimary
            ? ColorTranslator.FromHtml("#1D4ED8")
            : ColorTranslator.FromHtml("#E0F2FE");
        _buttons.Add(button);
        return button;
    }

    private void AddBackButton(TableLayoutPanel page, string destination)
    {
        Button back = NewButton("Quay lại");
        back.Click += (_, _) => ShowPage(destination);
        AddItem(page, back);
    }

    private void RegisterPage(string key, Control page)
    {
        _pages.Add(key, page);
        _pageHost.Controls.Add(page);
    }

    private void ShowPage(string key)
    {
        // Khong luu mat khau / OTP tren Control khi roi khoi buoc.
        if (_currentPage == "login" && key != "login")
            _loginPassword.Clear();
        if (_currentPage == "reset" && key != "reset")
        {
            _newPassword.Clear();
            _confirmPassword.Clear();
        }
        if (_currentPage == "otp" && key != "otp" && key != "reset")
            _otp.Clear();
        if (key == "login")
        {
            _otp.Clear();
            _recoveryStage = RecoveryStage.None;
        }

        _currentPage = key;
        _errors.Clear();
        foreach (KeyValuePair<string, Control> page in _pages)
            page.Value.Visible = page.Key == key;
        _pages[key].BringToFront();

        AcceptButton = key switch
        {
            "machine" => _handshakeButton,
            "login" => _loginButton,
            "client-dashboard" => _clientLogoutButton,
            "admin-dashboard" => _adminLogoutButton,
            "forgot" => _requestOtpButton,
            "otp" => _verifyOtpButton,
            "reset" => _resetButton,
            _ => null
        };
        UpdateButtonStates();
        SetStatus("Sẵn sàng. Chưa gửi yêu cầu tới Server.");
        if (IsHandleCreated)
        {
            Control focus = key switch
            {
                "machine" => _machineName,
                "login" => _loginUsername,
                "forgot" => _identifier,
                "otp" => _otp,
                "reset" => _newPassword,
                _ => _pageHost
            };
            focus.Focus();
        }
    }

    private void UpdateButtonStates()
    {
        foreach (Button button in _buttons)
            button.Enabled = !_busy;
        if (_resetButton is not null)
            _resetButton.Enabled = !_busy && _recoveryStage == RecoveryStage.OtpVerified;
        if (_clientLogoutButton is not null)
            _clientLogoutButton.Enabled = !_busy && _authenticated;
        if (_adminLogoutButton is not null)
            _adminLogoutButton.Enabled = !_busy && _authenticated;
        if (_machineLink is not null)
            _machineLink.Enabled = !_busy && !_authenticated;
        if (_handshakeButton is not null)
            _handshakeButton.Enabled = !_busy && !_authenticated;
        if (_loginButton is not null)
            _loginButton.Enabled = !_busy && !_authenticated;
    }

    private void InvalidateRecovery()
    {
        _recoveryStage = RecoveryStage.None;
        _otp.Clear();
        _newPassword.Clear();
        _confirmPassword.Clear();
        UpdateButtonStates();
    }

    private void InvalidateConnectionSettings()
    {
        if (!_machineVerified && !_authenticated)
        {
            return;
        }

        ResetConnection();
        ShowPage("login");
        SetStatus("Cấu hình Server đã thay đổi. Hãy xác thực thiết bị lại.");
    }

    private void UpdateDeviceBadge()
    {
        string mode = SelectedMode() == WorkstationMode.Administration
            ? "MÁY QUẢN TRỊ"
            : "MÁY KHÁCH";
        string machineName = string.IsNullOrWhiteSpace(_machineName.Text)
            ? "CHƯA ĐẶT TÊN"
            : _machineName.Text.Trim();
        _deviceBadge.Text = $"{mode}\n{machineName}";
        _deviceBadge.AccessibleName = $"{mode}, {machineName}";
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        UpdateButtonStates();
        _errors.Clear();
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            ResetConnection();
            SetStatus("Máy chủ không phản hồi đúng thời gian. Hãy kết nối lại.", error: true);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
        {
            ResetConnection();
            SetStatus(
                $"Không kết nối được tới Server tại {_serverHost.Text.Trim()}:{_serverPort.Text.Trim()}.",
                error: true);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            ResetConnection();
            SetStatus("Phản hồi từ Server không đúng định dạng giao thức.", error: true);
        }
        catch (InvalidOperationException ex)
        {
            // Chi hien thi thong bao nghiep vu; khong in mat khau/token/OTP.
            SetStatus(ex.Message, error: true);
        }
        catch (FormatException)
        {
            SetStatus("Khóa máy Base64 không đúng định dạng.", error: true);
        }
        catch (Exception)
        {
            // Khong hien thi stack trace, mat khau, OTP hoac du lieu goi tin.
            SetStatus("Có lỗi khi xử lý yêu cầu. Vui lòng kiểm tra Server.", error: true);
        }
        finally
        {
            _busy = false;
            UpdateButtonStates();
        }
    }

    // TCP ket noi dai han: server luu Machine/Authentication/Session theo ket noi.
    // Khong mo TcpClient moi cho moi AUTH_LOGIN/SESSION_START.
    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client is not null) return;
        string host = _serverHost.Text.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("Vui lòng nhập địa chỉ Server.");
        }

        int port = ReadServerPort();
        var client = new TcpClientAgent();
        try
        {
            await client.ConnectAsync(host, port, cancellationToken);
            _client = client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<NetworkMessage> SendAsync<T>(
        string type,
        T payload,
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken);
        SetStatus("Đang gửi yêu cầu tới Server...");
        var request = new NetworkMessage(
            Guid.NewGuid().ToString("N"),
            type,
            _machineName.Text.Trim(),
            null,
            JsonSerializer.SerializeToElement(payload, JsonOptions));
        NetworkMessage response = await _client!.SendRequestAsync(request, cancellationToken);
        string? error = ReadError(response);
        if (error is not null)
            throw new InvalidOperationException(error);
        if (response.Type != NetworkMessageTypes.CommandResult)
            throw new InvalidDataException("Server trả về loại thông điệp không hợp lệ.");
        return response;
    }

    private async Task<T> SendResultAsync<T>(
        string type,
        object payload,
        CancellationToken cancellationToken) where T : class
    {
        NetworkMessage response = await SendAsync(type, payload, cancellationToken);
        return response.Data.Deserialize<T>(JsonOptions)
               ?? throw new InvalidDataException("Server trả dữ liệu không hợp lệ.");
    }

    private void ResetConnection()
    {
        _client?.Dispose();
        _client = null;
        _activeSession = null;
        _machineVerified = false;
        _authenticated = false;
        _machineStatus.Text = "Máy chưa được xác thực.";
        UpdateButtonStates();
    }

    private void EnsureMachineVerified()
    {
        if (_client is null || !_machineVerified)
            throw new InvalidOperationException("Hãy xác thực máy trạm trước khi đăng nhập.");
    }

    private UserRole SelectedRole() =>
        _recoveryRole.SelectedItem is UserRole role ? role : UserRole.Client;

    private WorkstationMode SelectedMode() =>
        _deviceMode.SelectedItem is WorkstationMode mode
            ? mode
            : WorkstationMode.Client;

    private int ReadServerPort()
    {
        if (!int.TryParse(_serverPort.Text.Trim(), out int port) ||
            port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                "Cổng Server phải là số trong khoảng 1-65535.");
        }

        return port;
    }

    private async Task HandshakeAsync()
    {
        if (!Require(_serverHost, "Vui lòng nhập địa chỉ Server.")) return;
        int serverPort = ReadServerPort();
        if (!Require(_machineName, "Vui lòng nhập tên máy.")) return;
        if (!Require(_machineKey, "Vui lòng nhập khóa máy Base64.")) return;

        byte[] machineKey;
        try
        {
            machineKey = Convert.FromBase64String(_machineKey.Text.Trim());
        }
        catch (FormatException)
        {
            _machineKey.Clear();
            SetStatus("Khóa máy Base64 không hợp lệ.", error: true);
            return;
        }
        if (machineKey.Length < 32)
        {
            CryptographicOperations.ZeroMemory(machineKey);
            _machineKey.Clear();
            SetStatus("Khóa máy phải chứa ít nhất 32 byte.", error: true);
            return;
        }

        try
        {
            ResetConnection();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            SessionResult<MachineHandshakeChallenge> challenge =
                await SendResultAsync<SessionResult<MachineHandshakeChallenge>>(
                    NetworkMessageTypes.MachineHandshakeBegin,
                    new MachineHandshakeBeginRequest(_machineName.Text.Trim()),
                    timeout.Token);
            if (!challenge.IsSuccess || challenge.Value is null)
                throw new InvalidOperationException(challenge.Message);

            string proof = MachineHandshakeProof.ComputeBase64(machineKey, challenge.Value);
            SessionResult<VerifiedMachine> completed =
                await SendResultAsync<SessionResult<VerifiedMachine>>(
                    NetworkMessageTypes.MachineHandshakeComplete,
                    new MachineHandshakeCompleteRequest(challenge.Value.ChallengeId, proof),
                    timeout.Token);
            if (!completed.IsSuccess || completed.Value is null)
                throw new InvalidOperationException(completed.Message);

            _machineVerified = true;
            _machineStatus.Text = $"Đã xác thực: {completed.Value.MachineName}";
            _settings = DesktopSettings.Create(
                _serverHost.Text,
                serverPort,
                SelectedMode(),
                completed.Value.MachineName,
                _machineKey.Text);
            _settings.Save();
            UpdateDeviceBadge();
            ShowPage("login");
            SetStatus($"Máy {completed.Value.MachineName} đã xác thực. Có thể đăng nhập.", success: true);
        }
        catch
        {
            ResetConnection();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(machineKey);
        }
    }

    private async Task EndSessionAsync()
    {
        if (!_authenticated) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        if (_activeSession is not null)
        {
            SessionResult<UsageSessionSummary> ended =
                await SendResultAsync<SessionResult<UsageSessionSummary>>(
                    NetworkMessageTypes.SessionEnd,
                    new EndUsageSessionRequest(_activeSession.SessionId, "DangXuat"),
                    timeout.Token);
            if (!ended.IsSuccess)
                throw new InvalidOperationException(ended.Message);
            _activeSession = null;
        }

        // Server huy session token theo context va tra COMMAND_RESULT.
        await SendAsync(NetworkMessageTypes.AuthLogout, new { }, timeout.Token);
        _authenticated = false;
        _clientWelcome.Text = string.Empty;
        _clientSession.Text = string.Empty;
        _adminWelcome.Text = string.Empty;
        ShowPage("login");
        UpdateButtonStates();
        SetStatus("Đã kết thúc phiên và đăng xuất.", success: true);
    }

    private static string? ReadError(NetworkMessage response)
    {
        if (response.Type != "ERROR") return null;
        string code = "UNKNOWN";
        string message = "Server đã từ chối yêu cầu.";
        if (response.Data.ValueKind == JsonValueKind.Object)
        {
            if (response.Data.TryGetProperty("code", out JsonElement codeElement))
                code = codeElement.ToString();
            if (response.Data.TryGetProperty("message", out JsonElement messageElement))
                message = messageElement.ToString();
        }
        return code == "E001"
            ? "Server chưa triển khai lệnh này (E001)."
            : $"{message} ({code})";
    }

    private void SetStatus(string message, bool error = false, bool success = false)
    {
        _status.ForeColor = error
            ? Theme.Error
            : success ? Theme.Success : Theme.TextSecondary;
        _status.Text = message;
    }

    private bool Require(TextBox field, string message)
    {
        if (!string.IsNullOrWhiteSpace(field.Text)) return true;
        _errors.SetError(field, message);
        SetStatus(message, error: true);
        field.Focus();
        return false;
    }

    private async Task LoginAsync()
    {
        EnsureMachineVerified();
        if (!Require(_loginUsername, "Vui lòng nhập tên đăng nhập.")) return;
        if (!Require(_loginPassword, "Vui lòng nhập mật khẩu.")) return;

        string username = _loginUsername.Text.Trim();
        string password = _loginPassword.Text;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            LoginResponse result = await SendResultAsync<LoginResponse>(
                NetworkMessageTypes.AuthLogin,
                new LoginRequest(username, password),
                timeout.Token);
            if (!result.IsSuccess || result.User is null)
            {
                SetStatus(result.Message, error: true);
                MessageBox.Show(this, result.Message, "Đăng nhập thất bại",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool modeMatchesRole =
                SelectedMode() == WorkstationMode.Client &&
                result.User.Role == UserRole.Client ||
                SelectedMode() == WorkstationMode.Administration &&
                result.User.Role == UserRole.Administrator;
            if (!modeMatchesRole)
            {
                await SendAsync(NetworkMessageTypes.AuthLogout, new { }, timeout.Token);
                string requiredMode = result.User.Role == UserRole.Administrator
                    ? "Máy quản trị"
                    : "Máy khách";
                throw new InvalidOperationException(
                    $"Tài khoản này phải đăng nhập trên thiết bị được cấu hình là {requiredMode}.");
            }

            // Phien su dung phai mo tren dung TCP connection vua dang nhap.
            if (result.User.Role == UserRole.Client)
            {
                SessionResult<UsageSessionSummary> started =
                    await SendResultAsync<SessionResult<UsageSessionSummary>>(
                        NetworkMessageTypes.SessionStart, new { }, timeout.Token);
                if (!started.IsSuccess || started.Value is null)
                    throw new InvalidOperationException(started.Message);
                _activeSession = started.Value;
                _clientWelcome.Text =
                    $"Xin chào {result.User.Username}. Số dư hiện tại: " +
                    $"{started.Value.RemainingBalance:N0} đồng.";
                _clientSession.Text =
                    $"Phiên #{started.Value.SessionId} đã bắt đầu lúc " +
                    $"{started.Value.StartedAt.ToLocalTime():HH:mm:ss}.";
            }
            else
            {
                _adminWelcome.Text =
                    $"Xin chào {result.User.Username}. Bạn đã đăng nhập với quyền quản trị.";
            }
            _authenticated = true;
            ShowPage(result.User.Role == UserRole.Client
                ? "client-dashboard"
                : "admin-dashboard");
            UpdateButtonStates();
            SetStatus($"Đăng nhập thành công: {username}.", success: true);
        }
        finally
        {
            _loginPassword.Clear();
        }
    }

    private async Task RequestOtpAsync()
    {
        if (!Require(_identifier, "Vui lòng nhập tên đăng nhập hoặc email.")) return;
        string identifier = _identifier.Text.Trim();
        _recoveryStage = RecoveryStage.None;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        PasswordResetResponse result = await SendResultAsync<PasswordResetResponse>(
            NetworkMessageTypes.AuthForgotPassword,
            new RequestPasswordResetRequest(identifier, SelectedRole()),
            timeout.Token);
        if (result?.IsSuccess == true)
        {
            _recoveryStage = RecoveryStage.OtpRequested;
            _otp.Clear();
            ShowPage("otp");
            SetStatus(result.Message);
        }
        else
        {
            SetStatus(result?.Message ?? "Server không trả kết quả yêu cầu OTP hợp lệ.", error: true);
        }
    }

    private async Task VerifyOtpAsync()
    {
        if (_recoveryStage != RecoveryStage.OtpRequested)
        {
            SetStatus("Vui lòng yêu cầu OTP từ màn hình Quên mật khẩu trước.", error: true);
            return;
        }
        if (!Require(_otp, "Vui lòng nhập mã OTP.")) return;
        string otp = _otp.Text.Trim();
        if (otp.Length != 6 || !otp.All(c => c >= '0' && c <= '9'))
        {
            _errors.SetError(_otp, "Mã OTP phải gồm đúng 6 chữ số từ 0 đến 9.");
            SetStatus("Mã OTP phải gồm đúng 6 chữ số từ 0 đến 9.", error: true);
            return;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        PasswordResetResponse result = await SendResultAsync<PasswordResetResponse>(
            NetworkMessageTypes.AuthVerifyOtp,
            new VerifyPasswordResetOtpRequest(
                _identifier.Text.Trim(), SelectedRole(), otp),
            timeout.Token);
        if (result?.IsSuccess == true)
        {
            _recoveryStage = RecoveryStage.OtpVerified;
            ShowPage("reset");
            SetStatus(result.Message, success: true);
        }
        else
        {
            SetStatus(result?.Message ?? "Không thể xác minh OTP.", error: true);
        }
    }

    private async Task ResetPasswordAsync()
    {
        if (_recoveryStage != RecoveryStage.OtpVerified)
        {
            SetStatus("Bạn cần xác minh OTP thành công trước khi đặt lại mật khẩu.", error: true);
            return;
        }
        if (!Require(_newPassword, "Vui lòng nhập mật khẩu mới.")) return;
        if (!Require(_confirmPassword, "Vui lòng xác nhận mật khẩu mới.")) return;
        if (_newPassword.Text.Length is < 8 or > 128)
        {
            _errors.SetError(_newPassword, "Mật khẩu phải có 8 đến 128 ký tự.");
            SetStatus("Mật khẩu mới phải có từ 8 đến 128 ký tự.", error: true);
            return;
        }
        if (_newPassword.Text != _confirmPassword.Text)
        {
            _errors.SetError(_confirmPassword, "Hai mật khẩu không giống nhau.");
            SetStatus("Hai mật khẩu mới không trùng nhau.", error: true);
            return;
        }

        string password = _newPassword.Text;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            PasswordResetResponse result = await SendResultAsync<PasswordResetResponse>(
                NetworkMessageTypes.AuthResetPassword,
                new CompletePasswordResetRequest(
                    _identifier.Text.Trim(), SelectedRole(), _otp.Text.Trim(), password),
                timeout.Token);
            if (result?.IsSuccess == true)
            {
                _recoveryStage = RecoveryStage.None;
                _otp.Clear();
                ResetConnection();
                ShowPage("login");
                SetStatus(result.Message + " Hãy xác thực máy trước khi đăng nhập lại.", success: true);
                MessageBox.Show(this, result.Message, "NetCafeManager",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                SetStatus(result?.Message ?? "Không thể đặt lại mật khẩu.", error: true);
            }
        }
        finally
        {
            _newPassword.Clear();
            _confirmPassword.Clear();
        }
    }
}
