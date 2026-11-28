
using System.Drawing;
using System.Windows.Forms;

namespace NetCafeManager.Client;

internal static class Theme
{
    public static readonly Color Navy =
        ColorTranslator.FromHtml("#0F172A");

    public static readonly Color Primary =
        ColorTranslator.FromHtml("#2563EB");

    public static readonly Color Cyan =
        ColorTranslator.FromHtml("#0891B2");

    public static readonly Color Background =
        ColorTranslator.FromHtml("#F8FAFC");

    public static readonly Color Surface = Color.White;

    public static readonly Color TextSecondary =
        ColorTranslator.FromHtml("#64748B");

    public static readonly Color Border =
        ColorTranslator.FromHtml("#E2E8F0");

    public static readonly Color LightBlue =
        ColorTranslator.FromHtml("#DBEAFE");

    public static readonly Color Error =
        ColorTranslator.FromHtml("#B91C1C");

    public static readonly Color Success =
        ColorTranslator.FromHtml("#15803D");
}

internal static class PageUi
{
    internal static TextBox NewTextBox(
        string name, bool isPassword = false) => new()
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

    internal static TableLayoutPanel NewPage(
        string title, string description)
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

        page.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

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

    internal static CheckBox NewCheckBox(string name) => new()
    {
        Text = name,
        AccessibleName = name,
        AutoSize = true,
        ForeColor = Theme.TextSecondary,
        Font = new Font("Segoe UI", 9),
        Margin = new Padding(0, 0, 0, 12)
    };

    internal static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(470, 0),
        ForeColor = Theme.TextSecondary,
        Font = new Font("Segoe UI", 9),
        Margin = new Padding(0, 3, 0, 13)
    };

    internal static void AddField(
        TableLayoutPanel page, string label, Control field)
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

    internal static void AddItem(
        TableLayoutPanel page, Control control)
    {
        int row = page.RowCount++;

        page.RowStyles.Add(
            new RowStyle(SizeType.AutoSize));

        control.Dock = DockStyle.Top;

        page.Controls.Add(control, 0, row);
    }

    internal static Button NewButton(
        string text, bool isPrimary = false)
    {
        var button = new Button
        {
            Text = text,
            AccessibleName = text,
            Height = 43,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Margin = new Padding(0, 7, 0, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = isPrimary
                ? Theme.Primary : Theme.Background,
            ForeColor = isPrimary
                ? Theme.Surface : Theme.Navy,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand
        };

        button.FlatAppearance.BorderSize =
            isPrimary ? 0 : 1;

        button.FlatAppearance.BorderColor =
            isPrimary ? Theme.Primary : Theme.Border;

        button.FlatAppearance.MouseOverBackColor =
            isPrimary
                ? ColorTranslator.FromHtml("#1D4ED8")
                : ColorTranslator.FromHtml("#E0F2FE");

        return button;
    }
}
