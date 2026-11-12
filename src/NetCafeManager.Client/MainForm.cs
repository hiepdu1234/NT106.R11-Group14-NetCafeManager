using System.Text.Json;
using NetCafeManager.Client.Networking;
using NetCafeManager.Shared.Authentication;
using NetCafeManager.Shared.Networking;
using NetCafeManager.Shared.Sessions;

namespace NetCafeManager.Client;

public sealed class MainForm : Form
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly TextBox txtMachineName = new() { Text = "MAY-01" };
    private readonly TextBox txtMachineKey = new()
    {
        UseSystemPasswordChar = true,
        PlaceholderText = "Khóa máy Base64 từ script demo"
    };
    private readonly TextBox txtUsername = new();
    private readonly TextBox txtPassword = new()
    {
        UseSystemPasswordChar = true
    };
    private readonly TextBox txtIdentifier = new();
    private readonly ComboBox cboRole = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly TextBox txtOtp = new() { MaxLength = 6 };
    private readonly TextBox txtNewPassword = new()
    {
        UseSystemPasswordChar = true
    };
    private readonly Button btnHandshake = new()
    {
        Text = "1. Kết nối và xác thực máy",
        AutoSize = true
    };
    private readonly Button btnLogin = new()
    {
        Text = "2. Đăng nhập và mở phiên",
        AutoSize = true
    };
    private readonly Button btnEndSession = new()
    {
        Text = "Kết thúc phiên và đăng xuất",
        AutoSize = true,
        Enabled = false
    };
    private readonly Button btnRequestOtp = new()
    {
        Text = "Gửi OTP",
        AutoSize = true
    };
    private readonly Button btnVerifyOtp = new()
    {
        Text = "Xác minh OTP",
        AutoSize = true
    };
    private readonly Button btnResetPassword = new()
    {
        Text = "Đặt lại mật khẩu",
        AutoSize = true
    };
    private readonly Label lblResult = new()
    {
        Text = "Chưa kết nối Server.",
        AutoSize = true,
        MaximumSize = new Size(820, 0)
    };

    private TcpClientAgent? _client;
    private UsageSessionSummary? _activeSession;
    private bool _machineVerified;

    public MainForm()
    {
        Text = "NetCafeManager - Demo xác thực và phiên sử dụng";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 650);

        cboRole.Items.AddRange([
            UserRole.Client,
            UserRole.Administrator
        ]);
        cboRole.SelectedItem = UserRole.Client;

        Controls.Add(CreateLayout());

        btnHandshake.Click += BtnHandshake_Click;
        btnLogin.Click += BtnLogin_Click;
        btnEndSession.Click += BtnEndSession_Click;
        btnRequestOtp.Click += BtnRequestOtp_Click;
        btnVerifyOtp.Click += BtnVerifyOtp_Click;
        btnResetPassword.Click += BtnResetPassword_Click;
        FormClosed += (_, _) => _client?.Dispose();
    }

    private Control CreateLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            AutoScroll = true,
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateMachineGroup(), 0, 0);
        root.Controls.Add(CreateLoginGroup(), 0, 1);
        root.Controls.Add(CreateOtpGroup(), 0, 2);

        var resultPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(8)
        };
        resultPanel.Controls.Add(lblResult);
        root.Controls.Add(resultPanel, 0, 3);
        return root;
    }

    private Control CreateMachineGroup()
    {
        var group = new GroupBox
        {
            Text = "Máy trạm (Server chạy tại 127.0.0.1:5050)",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12)
        };
        var layout = CreateTwoColumnLayout();
        AddRow(layout, "Tên máy", txtMachineName);
        AddRow(layout, "Khóa máy Base64", txtMachineKey);
        AddRow(layout, string.Empty, btnHandshake);
        group.Controls.Add(layout);
        return group;
    }

    private Control CreateLoginGroup()
    {
        var group = new GroupBox
        {
            Text = "Đăng nhập và phiên sử dụng",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12)
        };
        var layout = CreateTwoColumnLayout();
        AddRow(layout, "Tên đăng nhập", txtUsername);
        AddRow(layout, "Mật khẩu", txtPassword);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        actions.Controls.Add(btnLogin);
        actions.Controls.Add(btnEndSession);
        AddRow(layout, string.Empty, actions);
        group.Controls.Add(layout);
        return group;
    }

    private Control CreateOtpGroup()
    {
        var group = new GroupBox
        {
            Text = "Khôi phục mật khẩu bằng OTP",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(12)
        };
        var layout = CreateTwoColumnLayout();
        AddRow(layout, "Tên đăng nhập/email", txtIdentifier);
        AddRow(layout, "Vai trò", cboRole);
        AddRow(layout, "OTP", txtOtp);
        AddRow(layout, "Mật khẩu mới", txtNewPassword);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        actions.Controls.Add(btnRequestOtp);
        actions.Controls.Add(btnVerifyOtp);
        actions.Controls.Add(btnResetPassword);
        AddRow(layout, string.Empty, actions);
        group.Controls.Add(layout);
        return group;
    }

    private static TableLayoutPanel CreateTwoColumnLayout() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 2,
        Padding = new Padding(4)
    };

    private static void AddRow(
        TableLayoutPanel layout,
        string label,
        Control control)
    {
        int row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 9, 12, 3)
        }, 0, row);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(3, 5, 3, 5);
        layout.Controls.Add(control, 1, row);
    }

    private async void BtnHandshake_Click(object? sender, EventArgs e)
    {
        await RunUiActionAsync(async cancellationToken =>
        {
            ResetConnection();
            _client = new TcpClientAgent();
            await _client.ConnectAsync("127.0.0.1", 5050, cancellationToken);

            SessionResult<MachineHandshakeChallenge> challengeResult =
                await SendAsync<SessionResult<MachineHandshakeChallenge>>(
                    NetworkMessageTypes.MachineHandshakeBegin,
                    new MachineHandshakeBeginRequest(txtMachineName.Text),
                    cancellationToken);
            if (!challengeResult.IsSuccess || challengeResult.Value is null)
            {
                throw new InvalidOperationException(challengeResult.Message);
            }

            byte[] machineKey = Convert.FromBase64String(
                txtMachineKey.Text.Trim());
            string proof = MachineHandshakeProof.ComputeBase64(
                machineKey,
                challengeResult.Value);
            SessionResult<VerifiedMachine> verified =
                await SendAsync<SessionResult<VerifiedMachine>>(
                    NetworkMessageTypes.MachineHandshakeComplete,
                    new MachineHandshakeCompleteRequest(
                        challengeResult.Value.ChallengeId,
                        proof),
                    cancellationToken);
            if (!verified.IsSuccess || verified.Value is null)
            {
                throw new InvalidOperationException(verified.Message);
            }

            _machineVerified = true;
            lblResult.Text =
                $"Máy {verified.Value.MachineName} đã xác thực thành công.";
        });
    }

    private async void BtnLogin_Click(object? sender, EventArgs e)
    {
        await RunUiActionAsync(async cancellationToken =>
        {
            EnsureMachineVerified();
            LoginResponse login = await SendAsync<LoginResponse>(
                NetworkMessageTypes.AuthLogin,
                new LoginRequest(txtUsername.Text, txtPassword.Text),
                cancellationToken);
            if (!login.IsSuccess || login.User is null)
            {
                throw new InvalidOperationException(login.Message);
            }

            if (login.User.Role == UserRole.Client)
            {
                SessionResult<UsageSessionSummary> started =
                    await SendAsync<SessionResult<UsageSessionSummary>>(
                        NetworkMessageTypes.SessionStart,
                        new { },
                        cancellationToken);
                if (!started.IsSuccess || started.Value is null)
                {
                    throw new InvalidOperationException(started.Message);
                }

                _activeSession = started.Value;
                btnEndSession.Enabled = true;
                lblResult.Text =
                    $"Đăng nhập và mở phiên #{started.Value.SessionId} thành công. " +
                    $"Số dư: {started.Value.RemainingBalance:N0} đồng.";
                return;
            }

            lblResult.Text = "Quản trị viên đăng nhập thành công.";
        });
    }

    private async void BtnEndSession_Click(object? sender, EventArgs e)
    {
        await RunUiActionAsync(async cancellationToken =>
        {
            if (_activeSession is not null)
            {
                SessionResult<UsageSessionSummary> ended =
                    await SendAsync<SessionResult<UsageSessionSummary>>(
                        NetworkMessageTypes.SessionEnd,
                        new EndUsageSessionRequest(
                            _activeSession.SessionId,
                            "DangXuat"),
                        cancellationToken);
                if (!ended.IsSuccess)
                {
                    throw new InvalidOperationException(ended.Message);
                }

                _activeSession = null;
            }

            await SendRawAsync(
                NetworkMessageTypes.AuthLogout,
                new { },
                cancellationToken);
            btnEndSession.Enabled = false;
            lblResult.Text = "Đã kết thúc phiên và đăng xuất.";
        });
    }

    private async void BtnRequestOtp_Click(object? sender, EventArgs e)
    {
        await RunUiActionAsync(async cancellationToken =>
        {
            await EnsureConnectedAsync(cancellationToken);
            PasswordResetResponse result =
                await SendAsync<PasswordResetResponse>(
                    NetworkMessageTypes.AuthForgotPassword,
                    new RequestPasswordResetRequest(
                        txtIdentifier.Text,
                        SelectedRole()),
                    cancellationToken);
            lblResult.Text = result.Message;
        });
    }

    private async void BtnVerifyOtp_Click(object? sender, EventArgs e)
    {
        await RunUiActionAsync(async cancellationToken =>
        {
            await EnsureConnectedAsync(cancellationToken);
            PasswordResetResponse result =
                await SendAsync<PasswordResetResponse>(
                    NetworkMessageTypes.AuthVerifyOtp,
                    new VerifyPasswordResetOtpRequest(
                        txtIdentifier.Text,
                        SelectedRole(),
                        txtOtp.Text),
                    cancellationToken);
            lblResult.Text = result.Message;
        });
    }

    private async void BtnResetPassword_Click(object? sender, EventArgs e)
    {
        await RunUiActionAsync(async cancellationToken =>
        {
            await EnsureConnectedAsync(cancellationToken);
            PasswordResetResponse result =
                await SendAsync<PasswordResetResponse>(
                    NetworkMessageTypes.AuthResetPassword,
                    new CompletePasswordResetRequest(
                        txtIdentifier.Text,
                        SelectedRole(),
                        txtOtp.Text,
                        txtNewPassword.Text),
                    cancellationToken);
            lblResult.Text = result.Message;
        });
    }

    private async Task<T> SendAsync<T>(
        string messageType,
        object payload,
        CancellationToken cancellationToken)
    {
        NetworkMessage response = await SendRawAsync(
            messageType,
            payload,
            cancellationToken);
        T? result = response.Data.Deserialize<T>(JsonOptions);
        return result ?? throw new InvalidDataException(
            "Server trả về dữ liệu không hợp lệ.");
    }

    private async Task<NetworkMessage> SendRawAsync(
        string messageType,
        object payload,
        CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Client chưa kết nối Server.");
        }

        var request = new NetworkMessage(
            Guid.NewGuid().ToString("N"),
            messageType,
            txtMachineName.Text.Trim(),
            null,
            JsonSerializer.SerializeToElement(payload, JsonOptions));
        NetworkMessage response = await _client.SendRequestAsync(
            request,
            cancellationToken);
        if (response.Type == NetworkMessageTypes.Error)
        {
            string message = response.Data.TryGetProperty(
                "message",
                out JsonElement messageElement)
                ? messageElement.GetString() ?? "Server từ chối yêu cầu."
                : "Server từ chối yêu cầu.";
            throw new InvalidOperationException(message);
        }

        return response;
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return;
        }

        _client = new TcpClientAgent();
        await _client.ConnectAsync("127.0.0.1", 5050, cancellationToken);
    }

    private void EnsureMachineVerified()
    {
        if (_client is null || !_machineVerified)
        {
            throw new InvalidOperationException(
                "Hãy kết nối và xác thực máy trạm trước.");
        }
    }

    private UserRole SelectedRole() =>
        cboRole.SelectedItem is UserRole role
            ? role
            : UserRole.Client;

    private async Task RunUiActionAsync(
        Func<CancellationToken, Task> action)
    {
        ToggleActions(false);
        try
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(20));
            await action(timeout.Token);
        }
        catch (Exception exception)
        {
            lblResult.Text = $"Thao tác thất bại: {exception.Message}";
        }
        finally
        {
            ToggleActions(true);
        }
    }

    private void ToggleActions(bool enabled)
    {
        btnHandshake.Enabled = enabled;
        btnLogin.Enabled = enabled;
        btnRequestOtp.Enabled = enabled;
        btnVerifyOtp.Enabled = enabled;
        btnResetPassword.Enabled = enabled;
        btnEndSession.Enabled = enabled && _activeSession is not null;
    }

    private void ResetConnection()
    {
        _client?.Dispose();
        _client = null;
        _activeSession = null;
        _machineVerified = false;
        btnEndSession.Enabled = false;
    }
}
