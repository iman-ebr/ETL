using Mapna.Contracts;

namespace Mapna.Sender;

public class PersonDetailForm : Form
{
    private static readonly Color ColorHeaderBg = Color.FromArgb(30, 41, 59);
    private static readonly Color ColorValidBg = Color.FromArgb(232, 245, 233);
    private static readonly Color ColorValidText = Color.FromArgb(22, 101, 52);
    private static readonly Color ColorInvalidBg = Color.FromArgb(255, 235, 238);
    private static readonly Color ColorInvalidText = Color.FromArgb(153, 27, 27);
    private static readonly Color ColorLabel = Color.FromArgb(100, 116, 139);
    private static readonly Color ColorValue = Color.FromArgb(15, 23, 42);
    private static readonly Color ColorSectionBorder = Color.FromArgb(226, 232, 240);
    private static readonly Color ColorSectionTitle = Color.FromArgb(37, 99, 235);
    private static readonly Color ColorEmptyValue = Color.FromArgb(180, 186, 196);
    private static readonly Color ColorBodyBg = Color.FromArgb(248, 250, 252);

    public PersonDetailForm(PersonnelRecord record, PersonnelValidator validator)
    {
        var validationResult = validator.Validate(record);
        BuildUi(record, validationResult.IsValid, validationResult.Errors.Select(e => e.ErrorMessage).ToList());
    }

    private void BuildUi(PersonnelRecord record, bool isValid, List<string> errorMessages)
    {
        Text = "جزئیات پرسنل";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        ClientSize = new Size(560, 660);
        MinimumSize = new Size(480, 500);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 9F);
        BackColor = ColorBodyBg;

        var header = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = ColorHeaderBg };

        var initials = BuildInitials(record.PerName, record.PerSurname);
        var avatar = new Label
        {
            Text = initials,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(56, 56),
            Location = new Point(20, 20)
        };
        avatar.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(Color.FromArgb(51, 65, 85));
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(brush, 0, 0, avatar.Width - 1, avatar.Height - 1);
        };

        var lblName = new Label
        {
            Text = $"{record.PerName} {record.PerSurname}",
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(88, 24)
        };
        var lblSubId = new Label
        {
            Text = $"شناسه پرسنلی: {record.PerId}    |    کد ملی: {record.NationalCode}",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize = true,
            Location = new Point(89, 54)
        };
        //header.Controls.Add(avatar);
        header.Controls.Add(lblName);
        header.Controls.Add(lblSubId);

        var bannerHeight = isValid ? 44 : 44 + (errorMessages.Count * 20) + 12;
        var banner = new Panel { Dock = DockStyle.Top, Height = bannerHeight, BackColor = isValid ? ColorValidBg : ColorInvalidBg };
        var lblStatus = new Label
        {
            Text = isValid ? "✔  This record is valid and ready to be sent" : "✘  This record will be rejected — see the reasons below:",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = isValid ? ColorValidText : ColorInvalidText,
            AutoSize = true,
            Location = new Point(18, 12)
        };
        banner.Controls.Add(lblStatus);

        if (!isValid)
        {
            var y = 38;
            foreach (var msg in errorMessages)
            {
                banner.Controls.Add(new Label
                {
                    Text = "•  " + msg,
                    Font = new Font("Segoe UI", 9F),
                    ForeColor = ColorInvalidText,
                    AutoSize = true,
                    Location = new Point(30, y)
                });
                y += 20;
            }
        }

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = ColorBodyBg,
            Padding = new Padding(16, 16, 16, 16)
        };

        var identitySection = BuildSection("اطلاعات هویتی", [
            ("نام", record.PerName),
            ("نام خانوادگی", record.PerSurname),
            ("نام به لاتین", record.PerLName),
            ("نام خانوادگی به لاتین", record.PerLSurname),
            ("جنسیت", record.SexCode == "M" ? "مرد" : record.SexCode == "F" ? "زن" : record.SexCode),
            ("تاریخ تولد", record.BornDate),
            ("کد ملی", record.NationalCode)
        ]);

        var contactSection = BuildSection("اطلاعات تماس", [
            ("ایمیل", record.PerEmail),
            ("موبایل", record.MobileNo),
            ("تلفن", record.Phone),
            ("آدرس", record.PerAddr)
        ]);

        var employmentSection = BuildSection("اطلاعات سازمانی", [
            ("وضعیت", record.PerStatus == 1 ? "فعال" : "غیرفعال"),
            ("نام کاربری", record.UserPrincipalName),
            ("شناسه شرکت", record.CompanyId?.ToString()),
            ("نوع قرارداد", record.PerContract)
        ]);

        var y2 = 0;
        foreach (var section in new[] { identitySection, contactSection, employmentSection })
        {
            section.Location = new Point(0, y2);
            section.Width = 512;
            body.Controls.Add(section);
            y2 += section.Height + 16;
        }

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.White };
        footer.Paint += (_, e) => e.Graphics.DrawLine(new Pen(ColorSectionBorder), 0, 0, footer.Width, 0);

        var btnCopy = new Button
        {
            Text = "کپی اطلاعات",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(51, 65, 85),
            Size = new Size(130, 32),
            Location = new Point(20, 12),
            Cursor = Cursors.Hand
        };
        btnCopy.FlatAppearance.BorderColor = ColorSectionBorder;
        btnCopy.Click += (_, _) => CopyToClipboard(record, isValid, errorMessages);

        var btnClose = new Button
        {
            Text = "بستن",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            Size = new Size(110, 32),
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.OK
        };
        btnClose.FlatAppearance.BorderSize = 0;
        void PositionCloseButton() => btnClose.Location = new Point(footer.Width - 130, 12);
        PositionCloseButton();
        footer.Resize += (_, _) => PositionCloseButton();

        footer.Controls.Add(btnCopy);
        footer.Controls.Add(btnClose);

        Controls.Add(body);
        Controls.Add(banner);
        Controls.Add(header);
        Controls.Add(footer);

        AcceptButton = btnClose;
    }

    private Panel BuildSection(string title, (string Label, string? Value)[] fields)
    {
        const int rowHeight = 44;
        const int titleHeight = 36;
        var height = titleHeight + fields.Length * rowHeight + 12;

        var section = new Panel
        {
            Height = height,
            BackColor = Color.White
        };
        section.Paint += (_, e) =>
        {
            using var pen = new Pen(ColorSectionBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, section.Width - 1, section.Height - 1);
        };

        var accent = new Panel { Location = new Point(0, 0), Size = new Size(4, titleHeight), BackColor = ColorSectionTitle };
        var lblTitle = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59),
            AutoSize = true,
            Location = new Point(16, 8)
        };
        section.Controls.Add(accent);
        section.Controls.Add(lblTitle);

        var y = titleHeight;
        var isAlternate = false;
        foreach (var (label, value) in fields)
        {
            if (isAlternate)
            {
                var stripe = new Panel
                {
                    Location = new Point(1, y),
                    Size = new Size(0, rowHeight), // width set on parent resize below
                    BackColor = Color.FromArgb(250, 250, 251)
                };
                stripe.Tag = "stripe";
                section.Controls.Add(stripe);
                section.Resize += (_, _) => stripe.Width = section.Width - 2;
                stripe.Width = section.Width - 2;
                stripe.SendToBack();
            }
            isAlternate = !isAlternate;

            var lblKey = new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = ColorLabel,
                AutoSize = true,
                Location = new Point(16, y + 6)
            };
            var lblVal = new Label
            {
                Text = string.IsNullOrWhiteSpace(value) ? "— (خالی)" : value,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = string.IsNullOrWhiteSpace(value) ? ColorEmptyValue : ColorValue,
                AutoSize = true,
                Location = new Point(16, y + 22),
                MaximumSize = new Size(480, 0)
            };
            section.Controls.Add(lblKey);
            section.Controls.Add(lblVal);

            y += rowHeight;
        }

        return section;
    }

    private static string BuildInitials(string? name, string? surname)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "" : name.Trim()[..1];
        var s = string.IsNullOrWhiteSpace(surname) ? "" : surname.Trim()[..1];
        var initials = (n + s);
        return string.IsNullOrEmpty(initials) ? "?" : initials;
    }

    private static void CopyToClipboard(PersonnelRecord record, bool isValid, List<string> errors)
    {
        var text = $"""
                {record.PerName} {record.PerSurname} (ID: {record.PerId})
                کدملی: {record.NationalCode}
                ایمیل: {record.PerEmail}
                موبایل: {record.MobileNo}
                وضعیت اعتبارسنجی: {(isValid ? "معتبر" : "نامعتبر — " + string.Join(" | ", errors))}
                """;
        Clipboard.SetText(text);
    }
}