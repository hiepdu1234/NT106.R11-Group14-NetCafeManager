using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using NetCafeManager.Client.Networking;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Networking;

namespace NetCafeManager.Client;


public sealed class MainForm : Form
{
    private const string ServerIp = "127.0.0.1";
    private const int ServerPort = 5050;
    private const string DemoMachineId = "MAY-01";

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
        Text = "Chế độ phát triển: chỉ thử bằng dữ liệu giả trên localhost."
    };

    private readonly TextBox _loginUsername = NewTextBox("Tên đăng nhập");
    private readonly TextBox _loginPassword = NewTextBox("Mật khẩu", isPassword: true);
    private readonly TextBox _identifier = NewTextBox("Tên đăng nhập hoặc email đã đăng ký");
    private readonly TextBox _otp = NewTextBox("Mã OTP gồm 6 chữ số");
    private readonly TextBox _newPassword = NewTextBox("Mật khẩu mới", isPassword: true);
    private readonly TextBox _confirmPassword = NewTextBox("Xác nhận mật khẩu mới", isPassword: true);

    private readonly ErrorProvider _errors = new();

    private Button? _loginButton;
    private Button? _requestOtpButton;
    private Button? _verifyOtpButton;
    private Button? _resetButton;
    private CheckBox? _showLoginPassword;
    private CheckBox? _showNewPasswords;
    private bool _busy;
    private RecoveryStage _recoveryStage;
    private string _currentPage = "";

    public MainForm()
    {
        Text = "NetCafeManager | Đăng nhập máy trạm";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        MinimumSize = new Size(840, 660);
        ClientSize = new Size(1000, 710);
        Font = new Font("Segoe UI", 10);
        BackColor = Theme.Background;

        _errors.ContainerControl = this;
        _errors.BlinkStyle = ErrorBlinkStyle.NeverBlink;
        FormClosed += (_, _) => _errors.Dispose();

        _otp.MaxLength = 6;
        _newPassword.MaxLength = 128;
        _confirmPassword.MaxLength = 128;
        _loginPassword.MaxLength = 128;

        // Neu nguoi dung sua tai khoan trong khi dang khoi phuc,
        // phai xac minh OTP lai; khong giu trang thai cu.
        _identifier.TextChanged += (_, _) => InvalidateRecovery();

        BuildLayout();
        BuildPages();
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
            Text = "Đăng nhập và khôi phục mật khẩu máy trạm",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(203, 213, 225),
            Font = new Font("Segoe UI", 10)
        };
        var machine = new Label
        {
            Text = $"MÃ MÁY  {DemoMachineId}\nBẢN DEMO LOCALHOST",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.Cyan,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            AccessibleName = $"Mã máy thử nghiệm {DemoMachineId}"
        };
        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(subtitle, 0, 1);
        header.Controls.Add(machine, 1, 0);
        header.SetRowSpan(machine, 2);
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
        // Man hinh 1: Dang nhap Client (Administrator thuoc ung dung Admin rieng).
        TableLayoutPanel login = NewPage(
            "ĐĂNG NHẬP", "Đăng nhập tài khoản người chơi để tiếp tục.");
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
        AddItem(login, Hint("Chỉ thử trên localhost bằng thông tin giả khi chưa có TLS."));
        RegisterPage("login", login);

        // Man hinh 2: Yeu cau OTP.
        TableLayoutPanel forgot = NewPage(
            "QUÊN MẬT KHẨU", "Nhập thông tin tài khoản để yêu cầu mã OTP.");
        AddField(forgot, "Tên đăng nhập hoặc email", _identifier);
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
            "login" => _loginButton,
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
                "login" => _loginUsername,
                "forgot" => _identifier,
                "otp" => _otp,
                "reset" => _newPassword,
                _ => _loginUsername
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
    }

    private void InvalidateRecovery()
    {
        _recoveryStage = RecoveryStage.None;
        _otp.Clear();
        _newPassword.Clear();
        _confirmPassword.Clear();
        UpdateButtonStates();
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
            SetStatus("Máy chủ không phản hồi đúng thời gian. Bạn có thể thử lại.", error: true);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
        {
            SetStatus("Không kết nối được tới Server tại localhost:5050.", error: true);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            SetStatus("Phản hồi từ Server không đúng định dạng giao thức.", error: true);
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

    private async Task<NetworkMessage> SendAsync<T>(string type, T payload)
    {
        SetStatus("Đang gửi yêu cầu tới Server...");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new TcpClientAgent();
        await client.ConnectAsync(ServerIp, ServerPort, timeout.Token);
        var request = new NetworkMessage(
            Guid.NewGuid().ToString("N"),
            type,
            DemoMachineId,
            null,
            JsonSerializer.SerializeToElement(payload, JsonOptions));
        return await client.SendRequestAsync(request, timeout.Token);
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

    private static T? ReadResult<T>(NetworkMessage response)
    {
        if (response.Type != "COMMAND_RESULT")
            return default;
        return response.Data.Deserialize<T>(JsonOptions);
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
        if (!Require(_loginUsername, "Vui lòng nhập tên đăng nhập.")) return;
        if (!Require(_loginPassword, "Vui lòng nhập mật khẩu.")) return;

        string username = _loginUsername.Text.Trim();
        string password = _loginPassword.Text;
        try
        {
            NetworkMessage response = await SendAsync(
                "AUTH_LOGIN", new LoginRequest(username, password));
            string? error = ReadError(response);
            if (error is not null)
            {
                SetStatus(error, error: true);
                return;
            }
            LoginResponse? result = ReadResult<LoginResponse>(response);
            if (result is null)
            {
                SetStatus("Server trả kết quả đăng nhập không hợp lệ.", error: true);
                return;
            }
            if (result.IsSuccess)
            {
                SetStatus($"Đăng nhập thành công: {result.User?.Username ?? username}.", success: true);
                MessageBox.Show(this,
                    "Đăng nhập thành công. Màn hình phiên sử dụng sẽ được tích hợp sau.",
                    "NetCafeManager", MessageBoxButtons.OK, MessageBoxIcon.Information);
                // Token va phien su dung do phan tich hop xu ly, khong log ra UI.
            }
            else
            {
                SetStatus(result.Message, error: true);
                MessageBox.Show(this, result.Message, "Đăng nhập thất bại",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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
        NetworkMessage response = await SendAsync(
            "AUTH_FORGOT_PASSWORD",
            new RequestPasswordResetRequest(identifier, UserRole.Client));
        string? error = ReadError(response);
        if (error is not null)
        {
            SetStatus(error, error: true);
            return;
        }
        PasswordResetResponse? result = ReadResult<PasswordResetResponse>(response);
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
        NetworkMessage response = await SendAsync(
            "AUTH_VERIFY_OTP",
            new VerifyPasswordResetOtpRequest(_identifier.Text.Trim(), UserRole.Client, otp));
        string? error = ReadError(response);
        if (error is not null)
        {
            SetStatus(error, error: true);
            return;
        }
        PasswordResetResponse? result = ReadResult<PasswordResetResponse>(response);
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
            NetworkMessage response = await SendAsync(
                "AUTH_RESET_PASSWORD",
                new CompletePasswordResetRequest(
                    _identifier.Text.Trim(), UserRole.Client, _otp.Text.Trim(), password));
            string? error = ReadError(response);
            if (error is not null)
            {
                SetStatus(error, error: true);
                return;
            }
            PasswordResetResponse? result = ReadResult<PasswordResetResponse>(response);
            if (result?.IsSuccess == true)
            {
                _recoveryStage = RecoveryStage.None;
                _otp.Clear();
                ShowPage("login");
                SetStatus(result.Message, success: true);
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
