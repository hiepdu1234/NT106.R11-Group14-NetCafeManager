
using System.Text.Json;
using NetCafeManager.Client.Networking;
using NetCafeManager.Shared.Networking;

namespace NetCafeManager.Client;

public sealed class MainForm : Form
{
    private readonly Button btnTestTcp = new()
    {
        Text = "Kiểm tra kết nối TCP",
        Location = new Point(30, 30),
        Size = new Size(220, 45)
    };

    private readonly Label lblResult = new()
    {
        Text = "Chưa kiểm tra kết nối.",
        Location = new Point(30, 95),
        Size = new Size(800, 60),
        AutoSize = false
    };

    public MainForm()
    {
        Text = "NetCafeManager - Máy khách";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);

        Controls.Add(btnTestTcp);
        Controls.Add(lblResult);

        btnTestTcp.Click += BtnTestTcp_Click;
    }

    private async void BtnTestTcp_Click(
        object? sender, EventArgs e)
    {
        btnTestTcp.Enabled = false;
        lblResult.Text = "Đang kết nối Server...";

        try
        {
            using var client = new TcpClientAgent();
            using var timeout =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(10));

            // Bước 1: Kết nối TCP
            await client.ConnectAsync(
                "127.0.0.1",
                5050,
                timeout.Token);

            // Bước 2: Tạo thông điệp kiểm thử
            NetworkMessage request = new(
                Guid.NewGuid().ToString("N"),
                "TEST_UNKNOWN",
                "MAY-01",
                null,
                JsonSerializer.SerializeToElement(
                    new { message = "TCP test" })
            );

            // Bước 3: Gửi và nhận phản hồi
            NetworkMessage response =
                await client.SendRequestAsync(
                    request,
                    timeout.Token);

            // Bước 4: Đọc mã lỗi từ phản hồi
            string? errorCode = null;

            if (response.Data.ValueKind == JsonValueKind.Object &&
                response.Data.TryGetProperty(
                    "code", out JsonElement codeElement))
            {
                errorCode = codeElement.GetString();
            }

            // Bước 5: Hiển thị kết quả
            if (response.Type == "ERROR" &&
                errorCode == "E001")
            {
                lblResult.Text =
                    "THÀNH CÔNG: Client đã gửi và nhận JSON qua TCP.";
            }
            else
            {
                lblResult.Text =
                    $"Phản hồi chưa đúng mong đợi: {response.Type}";
            }
        }
        catch (Exception ex)
        {
            lblResult.Text =
                $"Kiểm tra thất bại: {ex.Message}";
        }
        finally
        {
            btnTestTcp.Enabled = true;
        }
    }
}
