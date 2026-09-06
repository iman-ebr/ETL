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

        private System.Windows.Forms.Panel pnlHeader;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblProgressPercent;
        private System.Windows.Forms.DataGridView gridResults;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblTotal;
        private System.Windows.Forms.ToolStripStatusLabel lblSent;
        private System.Windows.Forms.ToolStripStatusLabel lblDuplicate;
        private System.Windows.Forms.ToolStripStatusLabel lblFailed;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPerId;
        private System.Windows.Forms.DataGridViewTextBoxColumn colName;
        private System.Windows.Forms.DataGridViewTextBoxColumn colStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn colDetail;

        private void InitializeComponent()
        {
            this.pnlHeader = new System.Windows.Forms.Panel();
            this.lblTitle = new System.Windows.Forms.Label();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblProgressPercent = new System.Windows.Forms.Label();
            this.gridResults = new System.Windows.Forms.DataGridView();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblTotal = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblSent = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblDuplicate = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblFailed = new System.Windows.Forms.ToolStripStatusLabel();
            this.colPerId = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colDetail = new System.Windows.Forms.DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)(this.gridResults)).BeginInit();
            this.pnlHeader.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();

            // pnlHeader
            this.pnlHeader.BackColor = System.Drawing.Color.FromArgb(33, 46, 63);
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.Height = 90;
            this.pnlHeader.Controls.Add(this.lblTitle);
            this.pnlHeader.Controls.Add(this.btnStart);
            this.pnlHeader.Controls.Add(this.btnCancel);
            this.pnlHeader.Controls.Add(this.progressBar);
            this.pnlHeader.Controls.Add(this.lblProgressPercent);

            // lblTitle
            this.lblTitle.AutoSize = true;
            this.lblTitle.ForeColor = System.Drawing.Color.White;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblTitle.Text = "سیستم همگام‌سازی اطلاعات پرسنلی";
            this.lblTitle.Location = new System.Drawing.Point(20, 15);

            // btnStart
            this.btnStart.Text = "شروع همگام‌سازی";
            this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStart.BackColor = System.Drawing.Color.FromArgb(46, 160, 92);
            this.btnStart.ForeColor = System.Drawing.Color.White;
            this.btnStart.FlatAppearance.BorderSize = 0;
            this.btnStart.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.btnStart.Size = new System.Drawing.Size(160, 36);
            this.btnStart.Location = new System.Drawing.Point(20, 45);
            this.btnStart.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);

            // btnCancel
            this.btnCancel.Text = "لغو";
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.BackColor = System.Drawing.Color.FromArgb(190, 60, 60);
            this.btnCancel.ForeColor = System.Drawing.Color.White;
            this.btnCancel.FlatAppearance.BorderSize = 0;
            this.btnCancel.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.btnCancel.Size = new System.Drawing.Size(90, 36);
            this.btnCancel.Location = new System.Drawing.Point(190, 45);
            this.btnCancel.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCancel.Enabled = false;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);

            // progressBar
            this.progressBar.Location = new System.Drawing.Point(300, 50);
            this.progressBar.Size = new System.Drawing.Size(420, 24);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;

            // lblProgressPercent
            this.lblProgressPercent.AutoSize = true;
            this.lblProgressPercent.ForeColor = System.Drawing.Color.White;
            this.lblProgressPercent.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.lblProgressPercent.Text = "آماده شروع";
            this.lblProgressPercent.Location = new System.Drawing.Point(735, 54);

            // gridResults
            this.gridResults.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridResults.BackgroundColor = System.Drawing.Color.White;
            this.gridResults.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.gridResults.AllowUserToAddRows = false;
            this.gridResults.AllowUserToDeleteRows = false;
            this.gridResults.ReadOnly = true;
            this.gridResults.RowHeadersVisible = false;
            this.gridResults.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.gridResults.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.gridResults.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(240, 242, 245);
            this.gridResults.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold);
            this.gridResults.ColumnHeadersHeight = 36;
            this.gridResults.RowTemplate.Height = 30;
            this.gridResults.EnableHeadersVisualStyles = false;
            this.gridResults.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
                this.colPerId, this.colName, this.colStatus, this.colDetail });

            // columns
            this.colPerId.HeaderText = "شناسه";
            this.colPerId.Name = "colPerId";
            this.colName.HeaderText = "نام و نام‌خانوادگی";
            this.colName.Name = "colName";
            this.colStatus.HeaderText = "وضعیت";
            this.colStatus.Name = "colStatus";
            this.colDetail.HeaderText = "توضیحات";
            this.colDetail.Name = "colDetail";

            // statusStrip
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.lblTotal, this.lblSent, this.lblDuplicate, this.lblFailed });
            this.lblTotal.Text = "کل رکوردها: ۰";
            this.lblSent.Text = "  |  ارسال‌شده: ۰";
            this.lblSent.ForeColor = System.Drawing.Color.FromArgb(46, 125, 50);
            this.lblDuplicate.Text = "  |  تکراری: ۰";
            this.lblDuplicate.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            this.lblFailed.Text = "  |  ناموفق: ۰";
            this.lblFailed.ForeColor = System.Drawing.Color.FromArgb(198, 40, 40);

            // Form1
            this.ClientSize = new System.Drawing.Size(900, 560);
            this.Controls.Add(this.gridResults);
            this.Controls.Add(this.pnlHeader);
            this.Controls.Add(this.statusStrip);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.RightToLeftLayout = true;
            this.Text = "MapnaEtl - همگام‌سازی پرسنلی";
            this.MinimumSize = new System.Drawing.Size(750, 450);

            ((System.ComponentModel.ISupportInitialize)(this.gridResults)).EndInit();
            this.pnlHeader.ResumeLayout(false);
            this.pnlHeader.PerformLayout();
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}