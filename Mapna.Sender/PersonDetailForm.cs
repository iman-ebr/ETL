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

    public PersonDetailForm(PersonnelRecord record,PersonnelValidator validator)
    {
        var validationResult = validator.Validate(record);
        BuildUi(record, validationResult.IsValid, validationResult.Errors.Select(e => e.ErrorMessage).ToList());
    }

    private void BuildUi(PersonnelRecord record, bool isValid, List<string> errorMessage)
    {
        Text = "Personnel Detail";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 620);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.White;

        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = ColorHeaderBg };
        var lblName = new Label
        {
            Text = $"{record.PerName} {record.PerSurname}",
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 14)
        };

        var lblSubId = new Label
        {
            Text = $"Personnel Id : {record.PerId}   |   National No: {record.NationalCode}",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize = true,
            Location = new Point(21, 46)
        };
        header.Controls.Add(lblName);
        header.Controls.Add(lblSubId);


        var banner = new Panel
        {
            Dock = DockStyle.Top,
            Height = isValid && errorMessage.Count == 0 ? 40 : 40 + (errorMessage.Count * 20) + 16,
            BackColor = isValid ? ColorValidBg : ColorInvalidBg
        };

        var lblStatus = new Label
        {
            Text = isValid ? "✔  This record is valid and ready to be sent" : "✘  This record will be rejected — see the reasons below:",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = isValid ? ColorValidText : ColorInvalidText,
            AutoSize = true,
            Location = new Point(16, 10)
        };

        banner.Controls.Add(lblStatus);


        if (!isValid)
        {
            var y = 36;
            foreach (var msg in errorMessage)
            {
                var lblErr = new Label
                {
                    Text = "•  " + msg,
                    Font = new Font("Segoe UI", 9F),
                    ForeColor = ColorInvalidText,
                    AutoSize = true,
                    Location = new Point(28, y)
                };
                banner.Controls.Add(lblErr);
                y += 20;
            }
        }

        var fieldsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.White,
            Padding = new Padding(20, 16, 20, 16)
        };



        var fields = new (string Label, string? Value)[]
        {
            ("Name", record.PerName),
            ("SurName", record.PerSurname),
            ("Latin Name", record.PerLName),
            ("Latin SurName", record.PerLSurname),
            ("Gender", record.SexCode == "M" ? "Male" : record.SexCode == "F" ? "Female" : record.SexCode),
            ("Birth Date", record.BornDate),
            ("National No", record.NationalCode),
            ("Status", record.PerStatus == 1 ? "Active" : "DeActive"),
            ("Email", record.PerEmail),
            ("Mobile", record.MobileNo),
            ("Phone", record.Phone),
            ("Address", record.PerAddr),
            ("UserName", record.UserPrincipalName),
            ("Company Id", record.CompanyId?.ToString()),
            ("Contract", record.PerContract),
            ("Contract", record.PerContract),
        };

        var y2 = 0;
        foreach (var (label, value) in fields)
        {
            var lblKey = new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = ColorLabel,
                AutoSize = true,
                Location = new Point(0, y2)
            };
            var lblVal = new Label
            {
                Text = string.IsNullOrWhiteSpace(value) ? "— (Empty)" : value,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = string.IsNullOrWhiteSpace(value) ? Color.FromArgb(180, 180, 180) : ColorValue,
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                Location = new Point(0, y2 + 18)
            };
            fieldsPanel.Controls.Add(lblKey);
            fieldsPanel.Controls.Add(lblVal);
            y2 += 52;
        }

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.FromArgb(248, 250, 252) };
        var btnCopy = new Button
        {
            Text = "Copy Info",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(51, 65, 85),
            Size = new Size(120, 30),
            Location = new Point(20, 11),
            Cursor = Cursors.Hand
        };
        btnCopy.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        btnCopy.Click += (_, _) => CopyToClipboard(record, isValid, errorMessage);

        var btnClose = new Button
        {
            Text = "Close",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            Size = new Size(100, 30),
            Location = new Point(400, 11),
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.OK
        };
        btnClose.FlatAppearance.BorderSize = 0;

        footer.Controls.Add(btnCopy);
        footer.Controls.Add(btnClose);

        Controls.Add(fieldsPanel);
        Controls.Add(banner);
        Controls.Add(header);
        Controls.Add(footer);

        AcceptButton = btnClose;
    }

    private static void CopyToClipboard(PersonnelRecord record, bool isValid, List<string> errors)
    {
        var text = $"""
                {record.PerName} {record.PerSurname} (ID: {record.PerId})
                National No: {record.NationalCode}
                Email: {record.PerEmail}
                Mobile: {record.MobileNo}
                Validation Status: {(isValid ? "Valid" : "Invalid — " + string.Join(" | ", errors))}
                """;
        Clipboard.SetText(text);
    }
}
