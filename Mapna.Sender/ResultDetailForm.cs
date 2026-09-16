namespace Mapna.Sender;

public class ResultDetailForm : Form
{
    private static readonly Color ColorHeaderBg = Color.FromArgb(30, 41, 59);
    private static readonly Color ColorLabel = Color.FromArgb(100, 116, 139);

    public ResultDetailForm(string title, List<RecordResult> results)
    {
        BuildUi(title, results);
    }

    private void BuildUi(string title, List<RecordResult> results)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ClientSize = new Size(760, 520);
        MinimumSize = new Size(560, 360);
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
        var lblCount = new Label
        {
            Text = $"{results.Count} record(s)",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize = true,
            Location = new Point(21, 40)
        };
        header.Controls.Add(lblTitle);
        header.Controls.Add(lblCount);

        var grid = new DataGridView
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
            ColumnHeadersHeight = 34,
            EnableHeadersVisualStyles = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        grid.RowTemplate.Height = 30;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 251);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        grid.DefaultCellStyle.SelectionForeColor = Color.Black;

        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "شناسه", DataPropertyName = "PerId", FillWeight = 12 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "نام", DataPropertyName = "PersonName", FillWeight = 25 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "وضعیت", DataPropertyName = "Status", FillWeight = 18 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "دلیل/جزئیات", DataPropertyName = "Reason", FillWeight = 45 });

        grid.DataSource = results;

        if (results.Count == 0)
        {
            grid.Visible = false;
            var lblEmpty = new Label
            {
                Text = "هیچ رکوردی در این دسته‌بندی وجود ندارد.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = ColorLabel,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            Controls.Add(lblEmpty);
        }

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
        btnClose.Location = new Point(footer.Width - 120, 11);
        footer.Resize += (_, _) => btnClose.Location = new Point(footer.Width - 120, 11);
        footer.Controls.Add(btnClose);

        Controls.Add(grid);
        Controls.Add(footer);
        Controls.Add(header);

        AcceptButton = btnClose;
        CancelButton = btnClose;
    }
}
