using Mapna.LogData;

namespace Mapna.Sender;

public class ResultDetailForm : Form
{
    private static readonly Color ColorHeaderBg = Color.FromArgb(30, 41, 59);
    private static readonly Color ColorLabel = Color.FromArgb(100, 116, 139);
    private static readonly Color ColorSent = Color.FromArgb(22, 163, 74);
    private static readonly Color ColorDuplicate = Color.FromArgb(100, 116, 139);
    private static readonly Color ColorFailed = Color.FromArgb(220, 38, 38);
    private static readonly Color ColorBorder = Color.FromArgb(226, 232, 240);

    private readonly List<RecordResult> _allResults;
    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private Label _lblCount = null!;

    public ResultDetailForm(string title, List<RecordResult> results)
    {
        _allResults = results;
        BuildUi(title);
        ApplyFilter(string.Empty);
    }

    private void BuildUi(string title)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ClientSize = new Size(820, 560);
        MinimumSize = new Size(600, 380);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.White;

        var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = ColorHeaderBg };
        var lblTitle = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 12)
        };
        _lblCount = new Label
        {
            Text = $"{_allResults.Count} رکورد",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize = true,
            Location = new Point(21, 40)
        };
        header.Controls.Add(lblTitle);
        header.Controls.Add(_lblCount);

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.White };
        var lblSearch = new Label
        {
            Text = "جستجو (شناسه، نام، دلیل):",
            AutoSize = true,
            ForeColor = ColorLabel,
            Location = new Point(16, 17)
        };
        _txtSearch = new TextBox { Location = new Point(190, 13), Size = new Size(280, 26) };
        _txtSearch.TextChanged += (_, _) => ApplyFilter(_txtSearch.Text);
        toolbar.Controls.Add(lblSearch);
        toolbar.Controls.Add(_txtSearch);
        toolbar.Paint += (_, e) => e.Graphics.DrawLine(new Pen(ColorBorder), 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            ColumnHeadersHeight = 36,
            EnableHeadersVisualStyles = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        _grid.RowTemplate.Height = 32;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 251);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        _grid.DefaultCellStyle.SelectionForeColor = Color.Black;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "شناسه", Name = "PerId", FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "نام", Name = "PersonName", FillWeight = 25 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "وضعیت", Name = "Status", FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "دلیل / جزئیات", Name = "Reason", FillWeight = 50 });

        _grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex != _grid.Columns["Status"]!.Index || e.RowIndex < 0) return;
            e.CellStyle!.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        };

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(header);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.FromArgb(248, 250, 252) };
        var btnClose = new Button
        {
            Text = "بستن",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            Size = new Size(100, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.OK
        };
        btnClose.FlatAppearance.BorderSize = 0;
        void PositionClose() => btnClose.Location = new Point(footer.Width - 120, 11);
        PositionClose();
        footer.Resize += (_, _) => PositionClose();
        footer.Controls.Add(btnClose);
        Controls.Add(footer);

        AcceptButton = btnClose;
        CancelButton = btnClose;
    }

    private void ApplyFilter(string term)
    {
        IEnumerable<RecordResult> filtered = _allResults;

        if (!string.IsNullOrWhiteSpace(term))
        {
            filtered = _allResults.Where(r =>
                r.PerId.ToString().Contains(term) ||
                r.PersonName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (r.Reason?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = filtered.ToList();
        _grid.Rows.Clear();
        _grid.Visible = list.Count > 0 || _allResults.Count > 0;

        foreach (var r in list)
        {
            var rowIndex = _grid.Rows.Add(r.PerId, r.PersonName, StatusText(r.Status), r.Reason ?? string.Empty);
            _grid.Rows[rowIndex].Cells["Status"].Style.ForeColor = StatusColor(r.Status);
        }

        _lblCount.Text = $"{list.Count} از {_allResults.Count} رکورد";

        if (_allResults.Count == 0)
        {
            ShowEmptyState("هیچ اجرایی هنوز انجام نشده است. پس از اجرای همگام‌سازی، نتایج اینجا نمایش داده می‌شوند.");
        }
        else if (list.Count == 0)
        {
            ShowEmptyState("هیچ رکوردی با این جستجو مطابقت ندارد.");
        }
        else
        {
            HideEmptyState();
        }
    }

    private Label? _emptyLabel;

    private void ShowEmptyState(string message)
    {
        _grid.Visible = false;
        if (_emptyLabel is null)
        {
            _emptyLabel = new Label
            {
                Font = new Font("Segoe UI", 10F),
                ForeColor = ColorLabel,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            Controls.Add(_emptyLabel);
            _emptyLabel.BringToFront();
        }
        _emptyLabel.Text = message;
        _emptyLabel.Visible = true;
    }

    private void HideEmptyState()
    {
        _grid.Visible = true;
        if (_emptyLabel is not null)
            _emptyLabel.Visible = false;
    }

    private static string StatusText(SendStatus status) => status switch
    {
        SendStatus.Sent => "ارسال شد",
        SendStatus.Duplicate => "تکراری — بدون تغییر",
        SendStatus.ValidationFailed => "نامعتبر",
        SendStatus.SendFailed => "ارسال ناموفق",
        _ => "نامشخص"
    };

    private static Color StatusColor(SendStatus status) => status switch
    {
        SendStatus.Sent => ColorSent,
        SendStatus.Duplicate => ColorDuplicate,
        SendStatus.ValidationFailed => ColorFailed,
        SendStatus.SendFailed => ColorFailed,
        _ => Color.Black
    };
}