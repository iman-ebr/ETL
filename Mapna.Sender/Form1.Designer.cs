namespace Mapna.Sender
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private System.Windows.Forms.TabControl tabControlMain;
        private System.Windows.Forms.TabPage tabDashboard;
        private System.Windows.Forms.TabPage tabDataExplorer;

        private System.Windows.Forms.Panel pnlHeader;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblSubtitle;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblProgressPercent;

        private System.Windows.Forms.Panel pnlStats;
        private System.Windows.Forms.Label lblValueTotal;
        private System.Windows.Forms.Label lblValueSent;
        private System.Windows.Forms.Label lblValueDuplicate;
        private System.Windows.Forms.Label lblValueFailed;

        private System.Windows.Forms.DataGridView gridResults;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPerId;
        private System.Windows.Forms.DataGridViewTextBoxColumn colName;
        private System.Windows.Forms.DataGridViewTextBoxColumn colStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn colDetail;

        private System.Windows.Forms.Panel pnlExplorerToolbar;
        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.Label lblSearchHint;
        private System.Windows.Forms.Button btnRefreshExplorer;
        private System.Windows.Forms.Label lblExplorerCount;
        private System.Windows.Forms.DataGridView gridExplorer;

        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblCurrentStatus;
        private System.Windows.Forms.ToolStripStatusLabel lblElapsed;
        private System.Windows.Forms.ToolStripStatusLabel lblThroughput;

        private static readonly System.Drawing.Color ColorHeaderBg = System.Drawing.Color.FromArgb(30, 41, 59);
        private static readonly System.Drawing.Color ColorSubtleText = System.Drawing.Color.FromArgb(148, 163, 184);
        private static readonly System.Drawing.Color ColorStatsBg = System.Drawing.Color.FromArgb(248, 250, 252);
        private static readonly System.Drawing.Color ColorAccentBlue = System.Drawing.Color.FromArgb(37, 99, 235);
        private static readonly System.Drawing.Color ColorAccentGreen = System.Drawing.Color.FromArgb(22, 163, 74);
        private static readonly System.Drawing.Color ColorAccentGray = System.Drawing.Color.FromArgb(100, 116, 139);
        private static readonly System.Drawing.Color ColorAccentRed = System.Drawing.Color.FromArgb(220, 38, 38);
        private static readonly System.Drawing.Color ColorInvalidRow = System.Drawing.Color.FromArgb(255, 235, 238);
        private static readonly System.Drawing.Color ColorBorder = System.Drawing.Color.FromArgb(226, 232, 240);

        private void InitializeComponent()
        {
            this.tabControlMain = new System.Windows.Forms.TabControl();
            this.tabDashboard = new System.Windows.Forms.TabPage();
            this.tabDataExplorer = new System.Windows.Forms.TabPage();

            this.pnlHeader = new System.Windows.Forms.Panel();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblSubtitle = new System.Windows.Forms.Label();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblProgressPercent = new System.Windows.Forms.Label();

            this.pnlStats = new System.Windows.Forms.Panel();

            this.gridResults = new System.Windows.Forms.DataGridView();
            this.colPerId = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colDetail = new System.Windows.Forms.DataGridViewTextBoxColumn();

            this.pnlExplorerToolbar = new System.Windows.Forms.Panel();
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.lblSearchHint = new System.Windows.Forms.Label();
            this.btnRefreshExplorer = new System.Windows.Forms.Button();
            this.lblExplorerCount = new System.Windows.Forms.Label();
            this.gridExplorer = new System.Windows.Forms.DataGridView();

            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblCurrentStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblElapsed = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblThroughput = new System.Windows.Forms.ToolStripStatusLabel();

            ((System.ComponentModel.ISupportInitialize)(this.gridResults)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridExplorer)).BeginInit();
            this.pnlHeader.SuspendLayout();
            this.pnlStats.SuspendLayout();
            this.pnlExplorerToolbar.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();

            this.pnlHeader.BackColor = ColorHeaderBg;
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.Height = 112;

            this.lblTitle.AutoSize = true;
            this.lblTitle.ForeColor = System.Drawing.Color.White;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 17F, System.Drawing.FontStyle.Bold);
            this.lblTitle.Text = "Mapna Personnel Sync";
            this.lblTitle.Location = new System.Drawing.Point(24, 14);

            this.lblSubtitle.AutoSize = true;
            this.lblSubtitle.ForeColor = ColorSubtleText;
            this.lblSubtitle.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.lblSubtitle.Text = "Source ERP  →  Receiver API";
            this.lblSubtitle.Location = new System.Drawing.Point(27, 50);

            this.btnStart.Text = "▶  Start Sync";
            this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStart.BackColor = ColorAccentGreen;
            this.btnStart.ForeColor = System.Drawing.Color.White;
            this.btnStart.FlatAppearance.BorderSize = 0;
            this.btnStart.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.btnStart.Size = new System.Drawing.Size(140, 32);
            this.btnStart.Location = new System.Drawing.Point(24, 76);
            this.btnStart.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);

            this.btnCancel.Text = "Cancel";
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.BackColor = ColorAccentRed;
            this.btnCancel.ForeColor = System.Drawing.Color.White;
            this.btnCancel.FlatAppearance.BorderSize = 0;
            this.btnCancel.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.btnCancel.Size = new System.Drawing.Size(96, 32);
            this.btnCancel.Location = new System.Drawing.Point(174, 76);
            this.btnCancel.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCancel.Enabled = false;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);

            this.progressBar.Location = new System.Drawing.Point(300, 80);
            this.progressBar.Size = new System.Drawing.Size(500, 22);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;

            this.lblProgressPercent.AutoSize = true;
            this.lblProgressPercent.ForeColor = System.Drawing.Color.White;
            this.lblProgressPercent.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblProgressPercent.Text = "Ready";
            this.lblProgressPercent.Location = new System.Drawing.Point(812, 83);

            this.pnlHeader.Controls.Add(this.lblTitle);
            this.pnlHeader.Controls.Add(this.lblSubtitle);
            this.pnlHeader.Controls.Add(this.btnStart);
            this.pnlHeader.Controls.Add(this.btnCancel);
            this.pnlHeader.Controls.Add(this.progressBar);
            this.pnlHeader.Controls.Add(this.lblProgressPercent);

            this.pnlStats.BackColor = ColorStatsBg;
            this.pnlStats.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlStats.Height = 92;

            CreateStatCard(this.pnlStats, "TOTAL RECORDS", ColorAccentBlue, 24, out this.lblValueTotal);
            CreateStatCard(this.pnlStats, "SENT", ColorAccentGreen, 270, out this.lblValueSent);
            CreateStatCard(this.pnlStats, "DUPLICATE (SKIPPED)", ColorAccentGray, 516, out this.lblValueDuplicate);
            CreateStatCard(this.pnlStats, "FAILED", ColorAccentRed, 762, out this.lblValueFailed);

            this.gridResults.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridResults.BackgroundColor = System.Drawing.Color.White;
            this.gridResults.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.gridResults.AllowUserToAddRows = false;
            this.gridResults.AllowUserToDeleteRows = false;
            this.gridResults.ReadOnly = true;
            this.gridResults.RowHeadersVisible = false;
            this.gridResults.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.gridResults.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.gridResults.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
            this.gridResults.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.gridResults.ColumnHeadersHeight = 36;
            this.gridResults.RowTemplate.Height = 30;
            this.gridResults.EnableHeadersVisualStyles = false;
            this.gridResults.AlternatingRowsDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(250, 250, 251);
            this.gridResults.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(219, 234, 254);
            this.gridResults.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.Black;
            this.gridResults.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
                this.colPerId, this.colName, this.colStatus, this.colDetail });

            this.colPerId.HeaderText = "ID";
            this.colPerId.Name = "colPerId";
            this.colPerId.FillWeight = 15;
            this.colName.HeaderText = "Name";
            this.colName.Name = "colName";
            this.colName.FillWeight = 30;
            this.colStatus.HeaderText = "Status";
            this.colStatus.Name = "colStatus";
            this.colStatus.FillWeight = 20;
            this.colDetail.HeaderText = "Details";
            this.colDetail.Name = "colDetail";
            this.colDetail.FillWeight = 55;

            this.tabDashboard.Controls.Add(this.gridResults);
            this.tabDashboard.Controls.Add(this.pnlStats);
            this.tabDashboard.Controls.Add(this.pnlHeader);
            this.tabDashboard.Text = "Dashboard";
            this.tabDashboard.BackColor = System.Drawing.Color.White;
            this.tabDashboard.Padding = new System.Windows.Forms.Padding(0);

            this.pnlExplorerToolbar.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlExplorerToolbar.Height = 56;
            this.pnlExplorerToolbar.BackColor = System.Drawing.Color.White;

            this.lblSearchHint.AutoSize = true;
            this.lblSearchHint.Text = "جستجو (نام، نام‌خانوادگی، کدملی):";
            this.lblSearchHint.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblSearchHint.ForeColor = System.Drawing.Color.FromArgb(71, 85, 105);
            this.lblSearchHint.Location = new System.Drawing.Point(20, 18);

            this.txtSearch.Location = new System.Drawing.Point(230, 14);
            this.txtSearch.Size = new System.Drawing.Size(260, 26);
            this.txtSearch.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.txtSearch.TextChanged += new System.EventHandler(this.txtSearch_TextChanged);

            this.btnRefreshExplorer.Text = "⟳  بروزرسانی از دیتابیس";
            this.btnRefreshExplorer.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRefreshExplorer.BackColor = ColorAccentBlue;
            this.btnRefreshExplorer.ForeColor = System.Drawing.Color.White;
            this.btnRefreshExplorer.FlatAppearance.BorderSize = 0;
            this.btnRefreshExplorer.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnRefreshExplorer.Size = new System.Drawing.Size(180, 30);
            this.btnRefreshExplorer.Location = new System.Drawing.Point(510, 13);
            this.btnRefreshExplorer.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRefreshExplorer.Click += new System.EventHandler(this.btnRefreshExplorer_Click);

            this.lblExplorerCount.AutoSize = true;
            this.lblExplorerCount.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.lblExplorerCount.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
            this.lblExplorerCount.Text = "در حال بارگذاری...";
            this.lblExplorerCount.Location = new System.Drawing.Point(710, 18);

            this.pnlExplorerToolbar.Controls.Add(this.lblSearchHint);
            this.pnlExplorerToolbar.Controls.Add(this.txtSearch);
            this.pnlExplorerToolbar.Controls.Add(this.btnRefreshExplorer);
            this.pnlExplorerToolbar.Controls.Add(this.lblExplorerCount);

            this.gridExplorer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridExplorer.BackgroundColor = System.Drawing.Color.White;
            this.gridExplorer.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.gridExplorer.AllowUserToAddRows = false;
            this.gridExplorer.AllowUserToDeleteRows = false;
            this.gridExplorer.ReadOnly = true;
            this.gridExplorer.RowHeadersVisible = false;
            this.gridExplorer.AutoGenerateColumns = false;
            this.gridExplorer.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.gridExplorer.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
            this.gridExplorer.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.gridExplorer.ColumnHeadersHeight = 34;
            this.gridExplorer.RowTemplate.Height = 28;
            this.gridExplorer.EnableHeadersVisualStyles = false;
            this.gridExplorer.AlternatingRowsDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(250, 250, 251);
            this.gridExplorer.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(219, 234, 254);
            this.gridExplorer.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.Black;

            AddExplorerColumn("PerId", "شناسه", 70);
            AddExplorerColumn("PerName", "نام", 90);
            AddExplorerColumn("PerSurname", "نام‌خانوادگی", 130);
            AddExplorerColumn("NationalCode", "کد ملی", 90);
            AddExplorerColumn("SexCode", "جنسیت", 60);
            AddExplorerColumn("BornDate", "تاریخ تولد", 90);
            AddExplorerColumn("PerStatus", "وضعیت", 60);
            AddExplorerColumn("PerEmail", "ایمیل", 190);
            AddExplorerColumn("MobileNo", "موبایل", 100);
            AddExplorerColumn("Phone", "تلفن", 90);
            AddExplorerColumn("PerAddr", "آدرس", 150);
            AddExplorerColumn("PerLName", "Latin Name", 90);
            AddExplorerColumn("PerLSurname", "Latin Surname", 100);
            AddExplorerColumn("UserPrincipalName", "یوزرنیم", 160);
            AddExplorerColumn("CompanyId", "کد شرکت", 80);
            AddExplorerColumn("PerContract", "نوع قرارداد", 130);

            this.gridExplorer.ShowCellToolTips = true;
            this.gridExplorer.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.gridExplorer_CellDoubleClick);
            this.gridExplorer.CellToolTipTextNeeded += new System.Windows.Forms.DataGridViewCellToolTipTextNeededEventHandler(this.gridExplorer_CellToolTipTextNeeded);
            this.gridExplorer.CellFormatting += new System.Windows.Forms.DataGridViewCellFormattingEventHandler(this.gridExplorer_CellFormatting);

            this.tabDataExplorer.Controls.Add(this.gridExplorer);
            this.tabDataExplorer.Controls.Add(this.pnlExplorerToolbar);

            this.tabDataExplorer.Text = "Data Explorer";
            this.tabDataExplorer.BackColor = System.Drawing.Color.White;
            this.tabDataExplorer.Padding = new System.Windows.Forms.Padding(0);

            this.tabControlMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControlMain.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.tabControlMain.Controls.Add(this.tabDashboard);
            this.tabControlMain.Controls.Add(this.tabDataExplorer);

            this.statusStrip.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.lblCurrentStatus, this.lblElapsed, this.lblThroughput });
            this.lblCurrentStatus.Text = "Ready";
            this.lblCurrentStatus.Font = new System.Drawing.Font("Segoe UI", 8.5F, System.Drawing.FontStyle.Bold);
            this.lblElapsed.Text = "  |  Elapsed: 00:00:00";
            this.lblThroughput.Text = "  |  0 records/sec";
            this.lblThroughput.Spring = true;
            this.lblThroughput.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            this.ClientSize = new System.Drawing.Size(1050, 660);
            this.Controls.Add(this.tabControlMain);
            this.Controls.Add(this.statusStrip);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.RightToLeftLayout = false;
            this.Text = "Mapna Personnel Sync";
            this.MinimumSize = new System.Drawing.Size(900, 560);
            this.Load += new System.EventHandler(this.Form1_Load);

            ((System.ComponentModel.ISupportInitialize)(this.gridResults)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gridExplorer)).EndInit();
            this.pnlHeader.ResumeLayout(false);
            this.pnlStats.ResumeLayout(false);
            this.pnlExplorerToolbar.ResumeLayout(false);
            this.pnlExplorerToolbar.PerformLayout();
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void AddExplorerColumn(string propertyName, string header, int width)
        {
            var col = new System.Windows.Forms.DataGridViewTextBoxColumn
            {
                Name = "colExp" + propertyName,
                DataPropertyName = propertyName,
                HeaderText = header,
                Width = width
            };
            this.gridExplorer.Columns.Add(col);
        }

        private void CreateStatCard(System.Windows.Forms.Panel container, string caption, System.Drawing.Color accent, int x, out System.Windows.Forms.Label valueLabel)
        {
            var card = new System.Windows.Forms.Panel
            {
                Location = new System.Drawing.Point(x, 14),
                Size = new System.Drawing.Size(230, 64),
                BackColor = System.Drawing.Color.White,
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle
            };

            var accentStrip = new System.Windows.Forms.Panel
            {
                Dock = System.Windows.Forms.DockStyle.Left,
                Width = 4,
                BackColor = accent
            };

            valueLabel = new System.Windows.Forms.Label
            {
                AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI", 19F, System.Drawing.FontStyle.Bold),
                ForeColor = System.Drawing.Color.FromArgb(30, 41, 59),
                Text = "0",
                Location = new System.Drawing.Point(16, 6)
            };

            var captionLabel = new System.Windows.Forms.Label
            {
                AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI", 8F),
                ForeColor = System.Drawing.Color.FromArgb(100, 116, 139),
                Text = caption,
                Location = new System.Drawing.Point(17, 40)
            };

            card.Controls.Add(valueLabel);
            card.Controls.Add(captionLabel);
            card.Controls.Add(accentStrip);

            container.Controls.Add(card);
        }
    }
}