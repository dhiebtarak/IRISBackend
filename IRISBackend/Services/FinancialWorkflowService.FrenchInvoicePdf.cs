using IRISBackend.Models;
using System.IO.Compression;
using System.Text;

namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService
{
    private static byte[] BuildFrenchContractDebitNotePdf(
        Contract contract,
        Counterparty cedent,
        string? contractType,
        string? mainCover,
        string documentReference,
        decimal contractGrossPremium,
        decimal cedentCommissionPercentage,
        decimal cedentCommissionAmount,
        decimal amountDue,
        int currencyDecimalPlaces,
        IrisInvoiceBrandingOptions branding,
        byte[]? logoBytes,
        IReadOnlyList<IrisInvoiceBankAccount> bankAccounts)
    {
        var currency = DisplayOrNotProvided(contract.IdCurrencyNavigation?.CurrencyCode);
        var drawing = new StringBuilder();
        var logo = logoBytes is null ? null : DecodePngForPdf(logoBytes);
        if (logo is not null)
        {
            const double logoWidth = 180d;
            var logoHeight = logoWidth * logo.Height / logo.Width;
            drawing.Append("q ")
                .Append(logoWidth.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 0 0 ")
                .Append(logoHeight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 48 720 cm /Logo Do Q\n");
        }

        AddText(drawing, branding.CompanyName, 335, 802, 12, bold: true);
        AddText(drawing, DisplayOrNotProvided(branding.AddressLine1), 335, 786, 8);
        AddText(drawing, DisplayOrNotProvided(branding.AddressLine2), 335, 774, 8);
        AddText(drawing, $"Tel. {DisplayOrNotProvided(branding.Phone)}", 335, 762, 8);
        AddCenteredText(drawing, "DEBIT NOTE", 735, 17, bold: true);
        AddRule(drawing, 48, 721, 499, 1.2m);

        DrawRect(drawing, 48, 610, 499, 98);
        DrawFillRect(drawing, 48, 686, 499, 22, 0.90m, 0.93m, 0.96m);
        AddText(drawing, "ISSUED TO", 60, 693, 9, bold: true);
        AddRule(drawing, 48, 686, 499, 0.7m);
        AddText(drawing, DisplayOrNotProvided(cedent.Name), 60, 669, 9, bold: true);
        AddText(drawing, $"Attention: {DisplayOrNotProvided(cedent.ContactName)}", 60, 654, 7);
        AddWrappedText(drawing, $"Address: {DisplayOrNotProvided(cedent.AddressLine)}", 60, 639, 7, 88, 9);
        AddText(drawing, $"City / Country: {DisplayOrNotProvided(JoinNonEmpty(cedent.City, cedent.Country))}", 60, 621, 7);
        AddText(drawing, "Debit Note Reference", 355, 669, 7, bold: true);
        AddText(drawing, documentReference, 355, 654, 8);
        AddText(drawing, "Issue Date", 355, 636, 7, bold: true);
        AddText(drawing, DateTime.UtcNow.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture), 420, 636, 8);

        DrawRect(drawing, 48, 478, 499, 122);
        DrawFillRect(drawing, 48, 578, 499, 22, 0.90m, 0.93m, 0.96m);
        AddText(drawing, "CONTRACT INFORMATION", 60, 585, 9, bold: true);
        AddRule(drawing, 48, 578, 499, 0.7m);
        var contractFields = new (string Label, string? Value, int LabelX, int ValueX, int ValueWidth)[]
        {
            ("Contract", contract.Name, 60, 130, 150), ("UMR", contract.Umr, 303, 350, 185),
            ("Insured", contract.Name, 60, 130, 150), ("Contract Type", contractType, 303, 385, 150),
            ("Main Cover", mainCover, 60, 130, 150), ("Contract Period", FormatPeriod(contract), 303, 385, 150),
            ("Transaction", "Original Premium", 60, 130, 150), ("Currency", currency, 303, 350, 185)
        };
        for (var index = 0; index < contractFields.Length; index++)
        {
            var field = contractFields[index];
            if (index % 2 == 0)
            {
                var y = 561 - (index / 2) * 23;
                AddText(drawing, field.Label, field.LabelX, y, 7, bold: true);
                DrawFillRect(drawing, field.ValueX, y - 5, field.ValueWidth, 16, 0.94m, 0.96m, 0.97m);
                DrawRect(drawing, field.ValueX, y - 5, field.ValueWidth, 16);
                AddText(drawing, DisplayOrNotProvided(field.Value), field.ValueX + 4, y, 7);
            }
            else
            {
                var y = 561 - ((index - 1) / 2) * 23;
                AddText(drawing, field.Label, field.LabelX, y, 7, bold: true);
                DrawFillRect(drawing, field.ValueX, y - 5, field.ValueWidth, 16, 0.94m, 0.96m, 0.97m);
                DrawRect(drawing, field.ValueX, y - 5, field.ValueWidth, 16);
                AddText(drawing, DisplayOrNotProvided(field.Value), field.ValueX + 4, y, 7);
            }
        }

        const int summaryTop = 467;
        const int summaryBottom = 337;
        DrawRect(drawing, 48, summaryBottom, 499, summaryTop - summaryBottom);
        DrawFillRect(drawing, 48, summaryTop - 22, 499, 22, 0.90m, 0.93m, 0.96m);
        AddText(drawing, "DEBIT NOTE SUMMARY", 60, summaryTop - 15, 9, bold: true);
        AddRule(drawing, 48, summaryTop - 22, 499, 0.7m);
        var financialRows = new List<(string Label, decimal Amount, string? Text, bool Emphasis)>
        {
            ("Contract Gross Premium", contractGrossPremium, null, false),
            ("Cedent Commission Percentage", 0m, $"{cedentCommissionPercentage.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%", false),
            ("Cedent Commission Amount", cedentCommissionAmount, null, false),
            ("Amount Due from Cedent", amountDue, null, true)
        };
        var rowY = summaryTop - 39;
        for (var index = 0; index < financialRows.Count; index++)
        {
            var row = financialRows[index];
            var isFinal = index == financialRows.Count - 1;
            if (isFinal)
                DrawFillRect(drawing, 56, rowY - 8, 483, 21, 0.88m, 0.92m, 0.95m);
            AddText(drawing, row.Label, 62, rowY, row.Emphasis ? 9 : 8, bold: row.Emphasis);
            if (row.Text is null)
            {
                AddText(drawing, currency, 385, rowY, row.Emphasis ? 9 : 8, bold: row.Emphasis);
                AddText(drawing, FormatAmount(row.Amount, currencyDecimalPlaces), 435, rowY, row.Emphasis ? 10 : 8, bold: true);
            }
            else
            {
                AddText(drawing, row.Text, 435, rowY, 8, bold: row.Emphasis);
            }
            if (!isFinal) AddRule(drawing, 60, rowY - 7, 475, 0.3m);
            rowY -= 19;
        }

        const int bankTop = 326;
        const int bankBottom = 158;
        DrawFillRect(drawing, 48, bankBottom, 499, bankTop - bankBottom, 1.00m, 0.97m, 0.78m);
        DrawRect(drawing, 48, bankBottom, 499, bankTop - bankBottom);
        AddCenteredText(drawing, "KINDLY REMIT YOUR PAYMENT TO ONE OF OUR BANK ACCOUNTS", bankTop - 15, 8, bold: true);
        AddRule(drawing, 48, bankTop - 24, 499, 0.7m);
        var accountRowHeight = (bankTop - bankBottom - 34d) / Math.Max(bankAccounts.Count, 1);
        for (var index = 0; index < bankAccounts.Count; index++)
        {
            var account = bankAccounts[index];
            var y = (int)(bankTop - 33 - accountRowHeight * index);
            AddText(drawing, $"{account.CurrencyCode}: {account.AccountHolder}", 58, y, 7, bold: true);
            AddText(drawing, $"Bank name: {account.BankName}", 58, y - 8, 6);
            var routing = JoinNonEmpty(
                string.IsNullOrWhiteSpace(account.Rib) ? null : $"RIB: {account.Rib}",
                string.IsNullOrWhiteSpace(account.Iban) ? null : $"IBAN: {account.Iban}");
            if (!string.IsNullOrWhiteSpace(routing)) AddText(drawing, routing, 58, y - 16, 6);
            var swiftAndDomiciliation = JoinNonEmpty(
                string.IsNullOrWhiteSpace(account.SwiftBic) ? null : $"SWIFT/BIC: {account.SwiftBic}");
            if (!string.IsNullOrWhiteSpace(swiftAndDomiciliation)) AddText(drawing, swiftAndDomiciliation, 58, y - 24, 6);
            if (!string.IsNullOrWhiteSpace(account.Domiciliation))
                AddText(drawing, $"Domiciliation: {account.Domiciliation}", 58, y - 32, 6);
        }

        AddText(drawing,
            "In order to comply with the payment terms defined by reinsurers, please make sure to remit the payment by : .../.../......",
            48, 137, 7, italic: true);
        AddRule(drawing, 48, 88, 499, 0.8m);
        AddText(drawing, JoinNonEmpty(branding.CompanyName, branding.AddressLine1, branding.AddressLine2), 48, 73, 6, bold: true);
        AddText(drawing, "Page 1 / 1", 494, 73, 6);

        var stream = Latin1.GetBytes(drawing.ToString());
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
        };
        var alphaObjectId = logo?.Alpha is null ? (int?)null : 7;
        var contentObjectId = logo is null ? 6 : 8;
        var resources = logo is null
            ? "<< /Font << /F1 4 0 R /F2 5 0 R >> >>"
            : "<< /Font << /F1 4 0 R /F2 5 0 R >> /XObject << /Logo 6 0 R >> >>";
        objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources {resources} /Contents {contentObjectId} 0 R >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
        if (logo is not null)
        {
            var softMask = alphaObjectId.HasValue ? $" /SMask {alphaObjectId.Value} 0 R" : string.Empty;
            objects.Add(PdfStream(
                $"/Type /XObject /Subtype /Image /Width {logo.Width} /Height {logo.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns {logo.Width} >>{softMask}",
                logo.Rgb));
            if (logo.Alpha is not null)
                objects.Add(PdfStream(
                    $"/Type /XObject /Subtype /Image /Width {logo.Width} /Height {logo.Height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 1 /BitsPerComponent 8 /Columns {logo.Width} >>",
                    logo.Alpha));
        }
        objects.Add(PdfStream(string.Empty, stream));
        return AssemblePdf(objects);
    }

    private static byte[] BuildFrenchAvoirCreditNotePdf(
        Contract contract,
        Counterparty cedent,
        string? contractType,
        string? mainCover,
        string? avoirLabel,
        string avoirNumber,
        DateOnly avoirDate,
        decimal avoirAmount,
        string currency,
        int currencyDecimalPlaces,
        IrisInvoiceBrandingOptions branding,
        byte[]? logoBytes)
    {
        var drawing = new StringBuilder();
        var logo = logoBytes is null ? null : DecodePngForPdf(logoBytes);
        if (logo is not null)
        {
            const double logoWidth = 180d;
            var logoHeight = logoWidth * logo.Height / logo.Width;
            drawing.Append("q ")
                .Append(logoWidth.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 0 0 ")
                .Append(logoHeight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 48 720 cm /Logo Do Q\n");
        }

        AddText(drawing, branding.CompanyName, 335, 802, 12, bold: true);
        AddText(drawing, DisplayOrNotProvided(branding.AddressLine1), 335, 786, 8);
        AddText(drawing, DisplayOrNotProvided(branding.AddressLine2), 335, 774, 8);
        AddText(drawing, $"Tel. {DisplayOrNotProvided(branding.Phone)}", 335, 762, 8);
        AddCenteredText(drawing, "CREDIT NOTE", 735, 17, bold: true);
        AddRule(drawing, 48, 721, 499, 1.2m);

        DrawRect(drawing, 48, 610, 499, 98);
        DrawFillRect(drawing, 48, 686, 499, 22, 0.90m, 0.93m, 0.96m);
        AddText(drawing, "ISSUED TO", 60, 693, 9, bold: true);
        AddRule(drawing, 48, 686, 499, 0.7m);
        AddText(drawing, DisplayOrNotProvided(cedent.Name), 60, 669, 9, bold: true);
        AddText(drawing, $"Attention: {DisplayOrNotProvided(cedent.ContactName)}", 60, 654, 7);
        AddWrappedText(drawing, $"Address: {DisplayOrNotProvided(cedent.AddressLine)}", 60, 639, 7, 88, 9);
        AddText(drawing, $"City / Country: {DisplayOrNotProvided(JoinNonEmpty(cedent.City, cedent.Country))}", 60, 621, 7);
        AddText(drawing, "Credit Note Reference", 355, 669, 7, bold: true);
        AddText(drawing, avoirNumber, 355, 654, 8);
        AddText(drawing, "Issue Date", 355, 636, 7, bold: true);
        AddText(drawing, avoirDate.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture), 420, 636, 8);

        DrawRect(drawing, 48, 478, 499, 122);
        DrawFillRect(drawing, 48, 578, 499, 22, 0.90m, 0.93m, 0.96m);
        AddText(drawing, "CONTRACT INFORMATION", 60, 585, 9, bold: true);
        AddRule(drawing, 48, 578, 499, 0.7m);
        var contractFields = new (string Label, string? Value, int LabelX, int ValueX, int ValueWidth)[]
        {
            ("Contract", contract.Name, 60, 130, 150), ("UMR", contract.Umr, 303, 350, 185),
            ("Insured", contract.Name, 60, 130, 150), ("Contract Type", contractType, 303, 385, 150),
            ("Main Cover", mainCover, 60, 130, 150), ("Contract Period", FormatPeriod(contract), 303, 385, 150),
            ("Transaction", avoirLabel, 60, 130, 150), ("Currency", currency, 303, 350, 185)
        };
        for (var index = 0; index < contractFields.Length; index++)
        {
            var field = contractFields[index];
            var y = 561 - (index / 2) * 23;
            AddText(drawing, field.Label, field.LabelX, y, 7, bold: true);
            DrawFillRect(drawing, field.ValueX, y - 5, field.ValueWidth, 16, 0.94m, 0.96m, 0.97m);
            DrawRect(drawing, field.ValueX, y - 5, field.ValueWidth, 16);
            AddText(drawing, DisplayOrNotProvided(field.Value), field.ValueX + 4, y, 7);
        }

        const int summaryTop = 467;
        const int summaryBottom = 337;
        DrawRect(drawing, 48, summaryBottom, 499, summaryTop - summaryBottom);
        DrawFillRect(drawing, 48, summaryTop - 22, 499, 22, 0.90m, 0.93m, 0.96m);
        AddText(drawing, "CREDIT NOTE SUMMARY", 60, summaryTop - 15, 9, bold: true);
        AddRule(drawing, 48, summaryTop - 22, 499, 0.7m);
        var amount = Math.Abs(avoirAmount);
        var amountY = summaryTop - 48;
        AddText(drawing, "Avoir Amount", 62, amountY, 9, bold: true);
        AddText(drawing, currency, 385, amountY, 9, bold: true);
        AddText(drawing, FormatAmount(amount, currencyDecimalPlaces), 435, amountY, 10, bold: true);
        var amountInWords = FrenchCurrencyAmountInWords.Convert(amount, currency, currencyDecimalPlaces);
        AddWrappedText(drawing,
            $"Arr\u00eat\u00e9 le pr\u00e9sent avoir \u00e0 la somme de : {amountInWords}.",
            62, amountY - 20, 7, 92, 9);

        AddRule(drawing, 48, 88, 499, 0.8m);
        AddText(drawing, JoinNonEmpty(branding.CompanyName, branding.AddressLine1, branding.AddressLine2), 48, 73, 6, bold: true);
        AddText(drawing, "Page 1 / 1", 494, 73, 6);

        var stream = Latin1.GetBytes(drawing.ToString());
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
        };
        var alphaObjectId = logo?.Alpha is null ? (int?)null : 7;
        var contentObjectId = logo is null ? 6 : 8;
        var resources = logo is null
            ? "<< /Font << /F1 4 0 R /F2 5 0 R >> >>"
            : "<< /Font << /F1 4 0 R /F2 5 0 R >> /XObject << /Logo 6 0 R >> >>";
        objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources {resources} /Contents {contentObjectId} 0 R >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
        if (logo is not null)
        {
            var softMask = alphaObjectId.HasValue ? $" /SMask {alphaObjectId.Value} 0 R" : string.Empty;
            objects.Add(PdfStream(
                $"/Type /XObject /Subtype /Image /Width {logo.Width} /Height {logo.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns {logo.Width} >>{softMask}",
                logo.Rgb));
            if (logo.Alpha is not null)
                objects.Add(PdfStream(
                    $"/Type /XObject /Subtype /Image /Width {logo.Width} /Height {logo.Height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 1 /BitsPerComponent 8 /Columns {logo.Width} >>",
                    logo.Alpha));
        }
        objects.Add(PdfStream(string.Empty, stream));
        return AssemblePdf(objects);
    }

    private static void AddText(StringBuilder drawing, string? value, int x, int y, int size, bool bold = false, bool italic = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var normalized = value.Replace('\u2019', '\'').Replace('\u2013', '-').Replace('\u2014', '-');
        var escaped = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (ch is '(' or ')' or '\\') escaped.Append('\\');
            escaped.Append(ch <= 255 ? ch : '?');
        }
        drawing.Append("0.07 0.12 0.19 rg BT /F").Append(bold ? '2' : '1').Append(' ').Append(size)
            .Append(" Tf 1 0 ").Append(italic ? "0.22" : "0").Append(" 1 ").Append(x).Append(' ').Append(y).Append(" Tm (")
            .Append(escaped).Append(") Tj ET\n");
    }

    private static void AddRule(StringBuilder drawing, int x, int y, int width, decimal lineWidth) =>
        drawing.Append("0.45 0.55 0.62 RG ").Append(lineWidth.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(" w ").Append(x).Append(' ').Append(y).Append(" m ").Append(x + width).Append(' ').Append(y).Append(" l S\n");

    private static void DrawRect(StringBuilder drawing, int x, int y, int width, int height) =>
        drawing.Append("0.35 0.43 0.50 RG 0.75 w ").Append(x).Append(' ').Append(y).Append(' ')
            .Append(width).Append(' ').Append(height).Append(" re S\n");

    private static void DrawFillRect(StringBuilder drawing, int x, int y, int width, int height, decimal red, decimal green, decimal blue) =>
        drawing.Append(red.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
            .Append(green.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
            .Append(blue.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" rg ")
            .Append(x).Append(' ').Append(y).Append(' ').Append(width).Append(' ').Append(height).Append(" re f\n");

    private static void AddCenteredText(StringBuilder drawing, string value, int centerY, int size, bool bold = false)
    {
        var approximateWidth = value.Length * size * 0.52;
        var x = (int)Math.Round((595d - approximateWidth) / 2d);
        AddText(drawing, value, x, centerY, size, bold);
    }

    private static int AddWrappedText(
        StringBuilder drawing,
        string? value,
        int x,
        int y,
        int size,
        int maxCharacters,
        int lineHeight,
        bool bold = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return y;
        foreach (var inputLine in value.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries))
        {
            var words = inputLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = new StringBuilder();
            foreach (var word in words)
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > maxCharacters)
                {
                    AddText(drawing, line.ToString(), x, y, size, bold);
                    y -= lineHeight;
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            if (line.Length > 0)
            {
                AddText(drawing, line.ToString(), x, y, size, bold);
                y -= lineHeight;
            }
        }
        return y;
    }

    private static string FormatAmount(decimal value) =>
        value.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

    private static string FormatAmount(decimal value, int decimalPlaces) =>
        value.ToString($"N{Math.Clamp(decimalPlaces, 0, 4)}", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))
            .Replace('\u202f', ' ')
            .Replace('\u00a0', ' ');

    private static string FormatPeriod(Contract contract)
    {
        var start = contract.InceptionDate?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        var end = contract.ExpiryDate?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        return start.Length == 0 && end.Length == 0 ? string.Empty : $"{start} - {end}";
    }

    private static string? JoinNonEmpty(params string?[] values) =>
        string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string JoinNonEmpty(string? first, string? second) =>
        string.Join(", ", new[] { first, second }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string DisplayOrNotProvided(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Not provided" : value.Trim();

    private static byte[] AssemblePdf(IReadOnlyList<byte[]> objects)
    {
        using var pdf = new MemoryStream();
        WriteAscii(pdf, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Position);
            WriteAscii(pdf, $"{i + 1} 0 obj\n");
            pdf.Write(objects[i]);
            WriteAscii(pdf, "\nendobj\n");
        }
        var xrefOffset = pdf.Position;
        WriteAscii(pdf, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++)
            WriteAscii(pdf, offsets[i].ToString("0000000000", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n");
        WriteAscii(pdf, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return pdf.ToArray();
    }

    private static byte[] PdfStream(string dictionaryEntries, byte[] bytes)
    {
        using var output = new MemoryStream();
        WriteAscii(output, $"<< {dictionaryEntries} /Length {bytes.Length} >>\nstream\n");
        output.Write(bytes);
        WriteAscii(output, "\nendstream");
        return output.ToArray();
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    private static Encoding Latin1 => Encoding.Latin1;
    private static void WriteAscii(Stream stream, string value) => stream.Write(Ascii(value));

    private static PngPdfImage? DecodePngForPdf(byte[] png)
    {
        if (png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return null;
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        using var compressed = new MemoryStream();
        for (var offset = 8; offset + 12 <= png.Length;)
        {
            var length = (int)ReadUInt32(png, offset);
            if (length < 0 || offset + 12L + length > png.Length) return null;
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IHDR")
            {
                width = (int)ReadUInt32(png, offset + 8);
                height = (int)ReadUInt32(png, offset + 12);
                bitDepth = png[offset + 16];
                colorType = png[offset + 17];
            }
            else if (type == "IDAT") compressed.Write(png, offset + 8, length);
            else if (type == "IEND") break;
            offset += length + 12;
        }
        if (width <= 0 || height <= 0 || bitDepth != 8 || colorType is not (2 or 6)) return null;
        var sourceBpp = colorType == 6 ? 4 : 3;
        var rowBytes = checked(width * sourceBpp);
        var expected = checked((rowBytes + 1) * height);
        compressed.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true)) zlib.CopyTo(inflated);
        var filtered = inflated.ToArray();
        if (filtered.Length != expected) return null;
        var raw = new byte[checked(rowBytes * height)];
        for (var y = 0; y < height; y++)
        {
            var filter = filtered[y * (rowBytes + 1)];
            var sourceRow = y * (rowBytes + 1) + 1;
            var targetRow = y * rowBytes;
            for (var x = 0; x < rowBytes; x++)
            {
                var left = x >= sourceBpp ? raw[targetRow + x - sourceBpp] : 0;
                var above = y > 0 ? raw[targetRow + x - rowBytes] : 0;
                var upperLeft = y > 0 && x >= sourceBpp ? raw[targetRow + x - rowBytes - sourceBpp] : 0;
                var predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => above,
                    3 => (left + above) / 2,
                    4 => Paeth(left, above, upperLeft),
                    _ => (byte)0
                };
                raw[targetRow + x] = unchecked((byte)(filtered[sourceRow + x] + predictor));
            }
        }

        using var rgbRows = new MemoryStream();
        using var alphaRows = colorType == 6 ? new MemoryStream() : null;
        for (var y = 0; y < height; y++)
        {
            rgbRows.WriteByte(0);
            alphaRows?.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                var p = y * rowBytes + x * sourceBpp;
                rgbRows.Write(raw, p, 3);
                if (alphaRows is not null) alphaRows.WriteByte(raw[p + 3]);
            }
        }
        return new PngPdfImage(width, height, Compress(rgbRows.ToArray()), alphaRows is null ? null : Compress(alphaRows.ToArray()));
    }

    private static byte Paeth(int left, int above, int upperLeft)
    {
        var p = left + above - upperLeft;
        var pa = Math.Abs(p - left);
        var pb = Math.Abs(p - above);
        var pc = Math.Abs(p - upperLeft);
        return (byte)(pa <= pb && pa <= pc ? left : pb <= pc ? above : upperLeft);
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(bytes);
        return output.ToArray();
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];

    private sealed record PngPdfImage(int Width, int Height, byte[] Rgb, byte[]? Alpha);
    private sealed record IrisInvoiceBankAccount(string? AccountHolder, string? BankName, string? CurrencyCode,
        string? Rib, string? Iban, string? SwiftBic, string? Domiciliation);
}
