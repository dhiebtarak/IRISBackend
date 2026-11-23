using System.Globalization;
using System.Text;

namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService
{
    // Collection notes share the accounting PDF engine, fonts and logo with invoices/Avoirs.
    private static byte[] BuildClaimCollectionPdf(IrisInvoiceAvoirPdfModel document)
    {
        var claim = document.ClaimDetails!;
        var pages = new List<StringBuilder>();
        var drawing = new StringBuilder();
        pages.Add(drawing);
        var logo = document.LogoBytes is null ? null : DecodePngForPdf(document.LogoBytes);
        if (logo is not null)
        {
            const double width = 107;
            var height = width * logo.Height / logo.Width;
            drawing.Append(FormattableString.Invariant($"q {width} 0 0 {height:0.###} 50 {779 - height:0.###} cm /Logo Do Q\n"));
        }
        var brand = document.Branding;
        var headerY = 775;
        void Header(string? text, bool bold = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            CollectionText(drawing, text, 350, headerY, 9, bold);
            headerY -= 11;
        }
        Header(brand.CompanyName, true);
        Header(brand.AddressLine1);
        Header(brand.AddressLine2);
        if (!string.IsNullOrWhiteSpace(brand.Phone)) Header("Tel. " + brand.Phone);
        Header("Fax. " + (string.IsNullOrWhiteSpace(brand.Fax) ? "+216 71 797251" : brand.Fax));
        var website = string.IsNullOrWhiteSpace(brand.Website) ? "www.impactrisks.com" : brand.Website;
        {
            drawing.Append("q 0 0 1 rg 0 0 1 RG\n");
            CollectionText(drawing, website, 350, headerY, 9, color: false);
            drawing.Append(FormattableString.Invariant($"0.4 w 350 {headerY - 1} m {350 + CollectionWidth(website, 9):0.###} {headerY - 1} l S Q\n"));
        }
        CollectionText(drawing, "Collection Note", 56, 638, 16, true);
        // Keep the real Avoir/claim references without introducing a separate claim numbering series.
        CollectionText(drawing, $"Source: Claim {claim.Number} | {document.DocumentNumber}", 56, 620, 7);
        var y = 591;
        void EnsureSpace(int needed)
        {
            if (y - needed >= 45) return;
            drawing = new StringBuilder(); pages.Add(drawing);
            CollectionText(drawing, "Collection Note (continued)", 56, 780, 14, true);
            CollectionText(drawing, $"{document.DocumentNumber} | {claim.Number}", 56, 762, 8);
            y = 734;
        }
        void Paragraph(string? value, bool bold = false, int size = 10, int lineHeight = 12)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            foreach (var line in CollectionLines(value, 483, size, bold))
            {
                EnsureSpace(lineHeight);
                CollectionText(drawing, line, 56, y, size, bold);
                y -= lineHeight;
            }
        }
        void Detail(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var offset = (int)Math.Ceiling(CollectionWidth(label + " ", 10));
            var lines = CollectionLines(value, 483 - offset, 10, true).ToArray();
            foreach (var (line, index) in lines.Select((line, index) => (line, index)))
            {
                EnsureSpace(12);
                if (index == 0) CollectionText(drawing, label, 56, y, 10);
                CollectionText(drawing, line, 56 + offset, y, 10, true);
                y -= 12;
            }
        }
        Paragraph(document.CedentName, true);
        Paragraph(JoinNonEmpty(document.CedentCity, document.CedentCountry));
        y -= 15;
        Detail("Reinsured:", claim.Cedent);
        // Retrocedant and Original Insured are not stored separately: omit missing fields.
        Detail("Type:", document.MainCover);
        Detail("Date Of Loss:", claim.LossDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        Detail("Description:", claim.Description);
        y = Math.Min(438, y - 60);
        EnsureSpace(100);
        CollectionText(drawing, "Details:", 56, y, 10);
        DrawAccountingLine(drawing, 56, y - 2, 33);
        y -= 7;
        DrawAccountingLine(drawing, 56, y, 483);
        y -= 13;
        CollectionText(drawing, "Being your share of the Claim/Survey Fee as described above", 56, y, 10);
        CollectionText(drawing, document.Currency, 384, y, 10);
        var amount = Math.Abs(document.Amount).ToString("N" + document.CurrencyDecimalPlaces, CultureInfo.GetCultureInfo("en-US"));
        CollectionText(drawing, amount, 539 - (int)Math.Ceiling(CollectionWidth(amount, 10)), y, 10);
        y -= 17;
        Paragraph(CollectionAmountInWords(Math.Abs(document.Amount), document.Currency, document.CurrencyDecimalPlaces), true);
        DrawAccountingLine(drawing, 56, y + 3, 483);
        y -= 24;
        EnsureSpace(35);
        Paragraph("KINDLY REMIT YOUR PAYMENT TO ONE OF OUR BANK ACCOUNTS:", true);
        y -= 10;
        foreach (var account in document.BankAccounts)
        {
            var lines = new List<string> { $"{account.AccountHolder} - {account.CurrencyCode}" };
            if (!string.IsNullOrWhiteSpace(account.Rib)) lines.Add("RIB: " + account.Rib);
            if (!string.IsNullOrWhiteSpace(account.Iban)) lines.Add("IBAN: " + account.Iban);
            if (!string.IsNullOrWhiteSpace(account.SwiftBic)) lines.Add("BIC: " + account.SwiftBic);
            lines.Add("Account domiciled at:");
            if (!string.IsNullOrWhiteSpace(account.BankName)) lines.Add(account.BankName);
            if (!string.IsNullOrWhiteSpace(account.Domiciliation)) lines.Add(account.Domiciliation);
            var wrapped = lines.SelectMany(line => CollectionLines(line, 483, 9, true)).ToArray();
            EnsureSpace(wrapped.Length * 11 + 10);
            foreach (var line in wrapped) Paragraph(line, true, 9, 11);
            y -= 10;
        }
        return AssembleAccountingPages(pages, document.LogoBytes);
    }

    private static void CollectionText(StringBuilder drawing, string value, int x, int y, int size, bool bold = false, bool color = true)
    {
        if (color) drawing.Append("0 0 0 rg ");
        var escaped = value.Replace("\r", " ").Replace("\n", " ").Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        drawing.Append($"BT /F{(bold ? 2 : 1)} {size} Tf 1 0 0 1 {x} {y} Tm ({escaped}) Tj ET\n");
    }

    // Standard Helvetica AFM widths; bold wrapping uses a conservative allowance.
    private static double CollectionWidth(string text, int size, bool bold = false)
    {
        int[] widths = [278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556,1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556,556,556,333,500,278,556,500,722,500,500,500,334,260,334,584];
        return text.Sum(c => c is >= ' ' and <= '~' ? widths[c - ' '] : 600) * size / 1000d * (bold ? 1.08 : 1);
    }

    private static IEnumerable<string> CollectionLines(string text, int width, int size, bool bold)
    {
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && CollectionWidth(line + " " + word, size, bold) > width)
                { yield return line; line = ""; }
                foreach (var c in word)
                {
                    if (CollectionWidth(line + c, size, bold) > width) { yield return line; line = ""; }
                    line += c;
                }
                line += " ";
            }
            if (!string.IsNullOrWhiteSpace(line)) yield return line.TrimEnd();
        }
    }

    internal static string CollectionAmountInWords(decimal amount, string currency, int precision)
    {
        amount = RoundMoney(amount, precision);
        // Reuse the English number spelling already used by closing documents.
        string Words(decimal value) => System.Text.RegularExpressions.Regex.Replace(AmountInWords(value, "", 0).Trim(), @"hundred (?!(?:thousand|million|billion|trillion)\b)", "hundred and ");
        var whole = decimal.Truncate(amount);
        var minor = decimal.Round((amount - whole) / PanelRemittanceCalculator.Unit(precision));
        var unit = currency.ToUpperInvariant() switch
        {
            "USD" => whole == 1 ? "dollar" : "dollars",
            "EUR" => whole == 1 ? "euro" : "euros",
            "TND" => whole == 1 ? "Tunisian dinar" : "Tunisian dinars",
            "GBP" => whole == 1 ? "pound sterling" : "pounds sterling",
            _ => currency
        };
        var fraction = minor == 0 ? "" : " and " + Words(minor) + (currency.Equals("TND", StringComparison.OrdinalIgnoreCase) ? " millimes" : precision == 2 ? " cents" : $" minor units (1/{1m / PanelRemittanceCalculator.Unit(precision):0})");
        return (Words(whole) + " " + unit + fraction).ToUpperInvariant();
    }
}
