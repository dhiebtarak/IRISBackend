using System.Text;

namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService
{
    private enum IrisAccountingDocumentType
    {
        Invoice,
        Avoir
    }

    private sealed record IrisInvoiceAvoirPdfModel(
        IrisAccountingDocumentType DocumentType,
        string DocumentNumber,
        DateOnly IssueDate,
        string CedentName,
        string? CedentAddress,
        string? CedentCity,
        string? CedentCountry,
        string? AccountManager,
        string? ContractName,
        string? Umr,
        string? Label,
        string? MainCover,
        DateOnly? InceptionDate,
        DateOnly? ExpiryDate,
        decimal Amount,
        string Currency,
        int CurrencyDecimalPlaces,
        IrisInvoiceBrandingOptions Branding,
        byte[]? LogoBytes,
        IReadOnlyList<IrisInvoiceBankAccount> BankAccounts);

    private static byte[] BuildIrisInvoiceAvoirPdf(IrisInvoiceAvoirPdfModel document)
    {
        var drawing = new StringBuilder();
        var logo = document.LogoBytes is null ? null : DecodePngForPdf(document.LogoBytes);
        if (logo is not null)
        {
            const double logoWidth = 180d;
            var logoHeight = logoWidth * logo.Height / logo.Width;
            drawing.Append("q ").Append(logoWidth.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 0 0 ").Append(logoHeight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 48 720 cm /Logo Do Q\n");
        }

        var branding = document.Branding;
        AddAccountingText(drawing, branding.CompanyName, 330, 800, 10, bold: true);
        AddAccountingText(drawing, branding.AddressLine1, 330, 785, 8);
        AddAccountingText(drawing, branding.AddressLine2, 330, 773, 8);
        AddAccountingText(drawing, $"Tel. {branding.Phone}", 330, 758, 8);
        AddAccountingText(drawing, $"Tunis, le {document.IssueDate:dd/MM/yyyy}", 48, 710, 9);

        DrawAccountingRect(drawing, 48, 620, 295, 68);
        AddAccountingWrappedText(drawing, document.CedentName, 60, 670, 9, 42, 11, bold: true);
        var addressY = AddAccountingWrappedText(drawing, document.CedentAddress, 60, 653, 7, 48, 9);
        AddAccountingText(drawing, JoinNonEmpty(document.CedentCity, document.CedentCountry), 60, Math.Min(635, addressY - 1), 7);
        AddAccountingText(drawing, "Affaire suivie par :", 365, 668, 8, bold: true);
        AddAccountingText(drawing, document.AccountManager, 365, 650, 8);

        var title = document.DocumentType == IrisAccountingDocumentType.Invoice
            ? $"FACTURE N\u00b0  {document.DocumentNumber}"
            : $"AVOIR N\u00b0    {document.DocumentNumber}";
        AddAccountingCenteredText(drawing, title, 582, 14, bold: true);

        DrawAccountingRect(drawing, 48, 418, 499, 128);
        AddAccountingText(drawing, "Objet", 62, 516, 8, bold: true);
        AddAccountingText(drawing, ":", 188, 516, 8);
        AddAccountingWrappedText(drawing,
            string.IsNullOrWhiteSpace(document.Label) ? "Non renseigné" : document.Label,
            203, 516, 8, 77, 10);
        AddAccountingText(drawing, "Nature de la garantie", 62, 481, 8, bold: true);
        AddAccountingText(drawing, ":", 188, 481, 8);
        AddAccountingText(drawing, document.MainCover, 203, 481, 8);
        AddAccountingText(drawing, "P\u00e9riode", 62, 446, 8, bold: true);
        AddAccountingText(drawing, ":", 188, 446, 8);
        var inception = document.InceptionDate?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        var expiry = document.ExpiryDate?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        AddAccountingText(drawing, $"du {inception} au {expiry}", 203, 446, 8);

        DrawAccountingRect(drawing, 48, 278, 499, 108);
        var firstAmountLabel = document.DocumentType == IrisAccountingDocumentType.Invoice ? "Prime nette" : "Remboursement";
        var secondAmountLabel = document.DocumentType == IrisAccountingDocumentType.Invoice ? "Prime TTC" : "Total à vous rembourser";
        var firstAmountY = 356;
        var secondAmountY = 298;
        AddAccountingText(drawing, firstAmountLabel, 62, firstAmountY, 9, bold: true);
        AddAccountingText(drawing, ":", 266, firstAmountY, 9);
        var displayAmount = document.DocumentType == IrisAccountingDocumentType.Avoir
            ? Math.Abs(document.Amount)
            : document.Amount;
        var formattedAmount = FormatFrenchAccountingAmount(displayAmount);
        AddAccountingRightAlignedText(drawing, formattedAmount, 529, firstAmountY, 10, 245, bold: true);
        var amountInWords = FrenchCurrencyAmountInWords.Convert(
            displayAmount, document.Currency, document.CurrencyDecimalPlaces);
        var amountSentence = document.DocumentType == IrisAccountingDocumentType.Invoice
            ? $"Arr\u00eat\u00e9e la pr\u00e9sente facture \u00e0 la somme de : {amountInWords}."
            : $"Arr\u00eat\u00e9 le pr\u00e9sent avoir \u00e0 la somme de : {amountInWords}.";
        AddAccountingWrappedText(drawing, amountSentence, 62, 340, 7, 120, 9);
        DrawAccountingLine(drawing, 48, 320, 499);
        AddAccountingText(drawing, secondAmountLabel, 62, secondAmountY, 9, bold: true);
        AddAccountingText(drawing, ":", 266, secondAmountY, 9);
        var currencyAndAmount = string.IsNullOrWhiteSpace(document.Currency)
            ? formattedAmount
            : $"{document.Currency}     {formattedAmount}";
        AddAccountingRightAlignedText(drawing, currencyAndAmount, 529, secondAmountY, 10, 245, bold: true);

        AddAccountingText(drawing,
            "Le paiement doit \u00eatre effectu\u00e9 par virement sur l'un de nos comptes indiqu\u00e9s ci-dessous.",
            48, 255, 7);

        var accounts = document.BankAccounts
            .Where(account => !string.IsNullOrWhiteSpace(account.BankName)
                && !string.IsNullOrWhiteSpace(account.CurrencyCode)
                && !string.IsNullOrWhiteSpace(account.SwiftBic)
                && !string.IsNullOrWhiteSpace(SelectBankAccountReference(account)))
            .ToArray();
        const int bankTop = 235;
        const int rowHeight = 19;
        var tableBottom = bankTop - 41 - rowHeight * accounts.Length;
        DrawAccountingRect(drawing, 48, tableBottom, 499, bankTop - tableBottom);
        AddAccountingCenteredText(drawing, "NOS COMPTES BANCAIRES", bankTop - 14, 8, bold: true);
        DrawAccountingLine(drawing, 48, bankTop - 22, 499);

        const int bankColumn = 220;
        const int currencyColumn = 278;
        const int accountColumn = 452;
        AddAccountingText(drawing, "Banque", 55, bankTop - 35, 7, bold: true);
        AddAccountingText(drawing, "Devise", 229, bankTop - 35, 7, bold: true);
        AddAccountingText(drawing, "IBAN / RIB", 287, bankTop - 35, 7, bold: true);
        AddAccountingText(drawing, "BIC / SWIFT", 461, bankTop - 35, 7, bold: true);
        DrawAccountingLine(drawing, 48, bankTop - 41, 499);
        DrawAccountingVerticalLine(drawing, bankColumn, bankTop - 22, tableBottom);
        DrawAccountingVerticalLine(drawing, currencyColumn, bankTop - 22, tableBottom);
        DrawAccountingVerticalLine(drawing, accountColumn, bankTop - 22, tableBottom);

        for (var index = 0; index < accounts.Length; index++)
        {
            var y = bankTop - 55 - rowHeight * index;
            var account = accounts[index];
            AddAccountingText(drawing, account.BankName, 55, y, 7);
            AddAccountingText(drawing, account.CurrencyCode, 229, y, 7);
            AddAccountingText(drawing, SelectBankAccountReference(account), 287, y, 7);
            AddAccountingText(drawing, account.SwiftBic, 461, y, 7);
            if (index < accounts.Length - 1) DrawAccountingLine(drawing, 48, y - 6, 499);
        }

        DrawAccountingLine(drawing, 48, 76, 499);
        var companyAddress = JoinNonEmpty(branding.AddressLine1, branding.AddressLine2);
        AddAccountingCenteredText(drawing, JoinNonEmpty(branding.CompanyName, companyAddress) ?? string.Empty, 60, 7);
        AddAccountingText(drawing, $"MF: {branding.TaxIdentifier}", 82, 42, 7);
        AddAccountingText(drawing, $"RC: {branding.CommercialRegister}", 300, 42, 7);
        AddAccountingText(drawing, "1 / 1", 510, 24, 7);

        var stream = Latin1.GetBytes(drawing.ToString());
        var objects = new List<byte[]>
        {
            Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            Ascii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Array.Empty<byte>(),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"),
            Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold /Encoding /WinAnsiEncoding >>")
        };

        int? logoObjectId = null;
        int? alphaObjectId = null;
        if (logo is not null)
        {
            logoObjectId = objects.Count + 1;
            alphaObjectId = logo.Alpha is null ? null : logoObjectId + 1;
            var softMask = alphaObjectId.HasValue ? $" /SMask {alphaObjectId.Value} 0 R" : string.Empty;
            objects.Add(PdfStream(
                $"/Type /XObject /Subtype /Image /Width {logo.Width} /Height {logo.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns {logo.Width} >>{softMask}",
                logo.Rgb));
            if (logo.Alpha is not null)
                objects.Add(PdfStream(
                    $"/Type /XObject /Subtype /Image /Width {logo.Width} /Height {logo.Height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 1 /BitsPerComponent 8 /Columns {logo.Width} >>",
                    logo.Alpha));
        }

        var contentObjectId = objects.Count + 1;
        objects.Add(PdfStream(string.Empty, stream));
        var xObjectResource = logoObjectId.HasValue ? $" /XObject << /Logo {logoObjectId.Value} 0 R >>" : string.Empty;
        objects[2] = Ascii(
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R /F2 5 0 R /F3 6 0 R /F4 7 0 R >>{xObjectResource} >> /Contents {contentObjectId} 0 R >>");
        return AssemblePdf(objects);
    }

    private static string? SelectBankAccountReference(IrisInvoiceBankAccount account)
    {
        if (string.Equals(account.CurrencyCode, "TND", StringComparison.OrdinalIgnoreCase))
            return JoinNonEmpty(account.Rib, account.Iban);
        return JoinNonEmpty(account.Iban, account.Rib);
    }

    private static void AddAccountingText(StringBuilder drawing, string? value, int x, int y, int size, bool bold = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var escaped = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (ch is '(' or ')' or '\\') escaped.Append('\\');
            escaped.Append(ch <= 255 ? ch : '?');
        }

        drawing.Append("0 0 0 rg BT /F").Append(bold ? '4' : '3').Append(' ').Append(size)
            .Append(" Tf 1 0 0 1 ").Append(x).Append(' ').Append(y).Append(" Tm (")
            .Append(escaped).Append(") Tj ET\n");
    }

    private static void AddAccountingRightAlignedText(
        StringBuilder drawing, string value, int rightX, int y, int preferredSize, int maxWidth, bool bold = false)
    {
        var fittedSize = Math.Min(preferredSize, Math.Max(6, (int)Math.Floor(maxWidth / (value.Length * 0.6d))));
        var textWidth = value.Length * fittedSize * 0.6d;
        var x = Math.Max(rightX - maxWidth, (int)Math.Floor(rightX - textWidth));
        AddAccountingText(drawing, value, x, y, fittedSize, bold);
    }

    private static string FormatFrenchAccountingAmount(decimal value) =>
        value.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))
            .Replace('\u202f', ' ')
            .Replace('\u00a0', ' ');

    private static void AddAccountingCenteredText(StringBuilder drawing, string value, int y, int size, bool bold = false)
    {
        var x = (int)Math.Round((595d - value.Length * size * 0.6d) / 2d);
        AddAccountingText(drawing, value, x, y, size, bold);
    }

    private static int AddAccountingWrappedText(
        StringBuilder drawing, string? value, int x, int y, int size, int maxCharacters, int lineHeight, bool bold = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return y;
        foreach (var inputLine in value.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = new StringBuilder();
            foreach (var word in inputLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > maxCharacters)
                {
                    AddAccountingText(drawing, line.ToString(), x, y, size, bold);
                    y -= lineHeight;
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }

            if (line.Length > 0)
            {
                AddAccountingText(drawing, line.ToString(), x, y, size, bold);
                y -= lineHeight;
            }
        }

        return y;
    }

    private static void DrawAccountingRect(StringBuilder drawing, int x, int y, int width, int height) =>
        drawing.Append("0 0 0 RG 0.55 w ").Append(x).Append(' ').Append(y).Append(' ')
            .Append(width).Append(' ').Append(height).Append(" re S\n");

    private static void DrawAccountingLine(StringBuilder drawing, int x, int y, int width) =>
        drawing.Append("0 0 0 RG 0.45 w ").Append(x).Append(' ').Append(y).Append(" m ")
            .Append(x + width).Append(' ').Append(y).Append(" l S\n");

    private static void DrawAccountingVerticalLine(StringBuilder drawing, int x, int top, int bottom) =>
        drawing.Append("0 0 0 RG 0.4 w ").Append(x).Append(' ').Append(bottom).Append(" m ")
            .Append(x).Append(' ').Append(top).Append(" l S\n");

}

