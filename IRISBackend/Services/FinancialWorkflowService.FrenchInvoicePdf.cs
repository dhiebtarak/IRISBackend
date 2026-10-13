using IRISBackend.Models;
using System.IO.Compression;
using System.Text;

namespace IRISBackend.Services;

public sealed partial class FinancialWorkflowService
{
    private static byte[] BuildFrenchCedentInvoicePdf(
        Invoice invoice,
        decimal grossAmount,
        decimal cedentCommissionAmount,
        decimal netAmount,
        IrisInvoiceBrandingOptions branding,
        byte[]? logoBytes,
        IReadOnlyList<IrisInvoiceBankAccount> bankAccounts)
    {
        var currency = invoice.IdCurrencyNavigation?.CurrencyCode ?? string.Empty;
        var contract = invoice.IdContractNavigation;
        var cedent = contract.IdCounterpartyNavigation;
        var drawing = new StringBuilder();
        var logo = logoBytes is null ? null : DecodePngForPdf(logoBytes);

        if (logo is not null) drawing.Append("q 230 0 0 84 48 731 cm /Logo Do Q\n");

        AddWrappedText(drawing, branding.CompanyName, 300, 800, 12, 38, 14, bold: true);
        AddText(drawing, branding.AddressLine1, 300, 780, 8);
        AddWrappedText(drawing, branding.AddressLine2, 300, 768, 8, 54, 10);
        AddText(drawing, $"Tél. {branding.Phone}", 300, 746, 8);
        AddText(drawing, $"Tunis, le {invoice.InvoiceDate:dd/MM/yyyy}", 340, 728, 9);

        DrawRect(drawing, 340, 658, 207, 57);
        AddText(drawing, cedent?.Name, 350, 698, 9, bold: true);
        var cedentAddressBottom = AddWrappedText(drawing, cedent?.AddressLine, 350, 682, 7, 44, 9);
        AddText(drawing, JoinNonEmpty(cedent?.City, cedent?.Country), 350, Math.Max(661, cedentAddressBottom - 2), 7);

        AddCenteredText(drawing, $"FACTURE N\u00b0 {invoice.InvoiceNumber}", 609, 15, bold: true);

        DrawRect(drawing, 48, 452, 499, 119);
        AddText(drawing, "INFORMATIONS DU CONTRAT", 60, 550, 9, bold: true);
        AddRule(drawing, 48, 540, 499, 0.5m);
        AddText(drawing, "Objet", 60, 521, 8, bold: true);
        AddText(drawing, contract.Name, 175, 521, 8);
        AddText(drawing, "Nature de la garantie", 60, 501, 8, bold: true);
        AddText(drawing, contract.ContractCoverageSelections
            .Where(selection => selection.IsPrimary)
            .Select(selection => selection.IdCoverageItemNavigation.CoverageName)
            .FirstOrDefault() ?? string.Empty, 175, 501, 8);
        AddText(drawing, "P\u00e9riode", 60, 481, 8, bold: true);
        AddText(drawing, $"du {contract.InceptionDate:dd/MM/yyyy} au {contract.ExpiryDate:dd/MM/yyyy}", 175, 481, 8);

        DrawRect(drawing, 48, 341, 499, 91);
        AddCenteredText(drawing, "PRIME NETTE", 402, 13, bold: true);
        AddCenteredText(drawing, FormatAmount(netAmount), 370, 20, bold: true);
        DrawRect(drawing, 48, 297, 499, 36);
        AddText(drawing, "Prime TTC", 60, 310, 9, bold: true);
        AddText(drawing, currency, 355, 310, 9);
        AddText(drawing, FormatAmount(netAmount), 460, 310, 10, bold: true);

        AddText(drawing, "Le paiement doit \u00eatre effectu\u00e9 par virement sur l\u2019un de nos comptes indiqu\u00e9s ci-dessous.", 48, 267, 8);
        var bankTop = 245;
        const int footerTop = 70;
        var rows = Math.Max(bankAccounts.Count, 1);
        var rowHeight = Math.Min(13d, Math.Max(5d, (bankTop - footerTop - 48d) / rows));
        var bankBottom = (int)Math.Max(footerTop, bankTop - 42d - rowHeight * rows);
        DrawRect(drawing, 48, bankBottom, 499, bankTop - bankBottom);
        AddCenteredText(drawing, "NOS COMPTES BANCAIRES", bankTop - 16, 9, bold: true);
        AddRule(drawing, 48, bankTop - 23, 499, 0.5m);
        AddText(drawing, "Banque", 55, bankTop - 36, 7, bold: true);
        AddText(drawing, "Devise", 180, bankTop - 36, 7, bold: true);
        AddText(drawing, "IBAN", 245, bankTop - 36, 7, bold: true);
        AddText(drawing, "BIC / SWIFT", 430, bankTop - 36, 7, bold: true);
        AddRule(drawing, 48, bankTop - 42, 499, 0.35m);
        for (var index = 0; index < bankAccounts.Count; index++)
        {
            var y = (int)(bankTop - 52 - rowHeight * index);
            var account = bankAccounts[index];
            var rowFontSize = (int)Math.Min(7d, Math.Max(4d, rowHeight - 2d));
            AddText(drawing, account.BankName, 55, y, rowFontSize);
            AddText(drawing, account.CurrencyCode, 180, y, rowFontSize);
            AddText(drawing, account.Iban, 245, y, rowFontSize);
            AddText(drawing, account.SwiftBic, 430, y, rowFontSize);
            if (index < bankAccounts.Count - 1) AddRule(drawing, 48, (int)(y - 4), 499, 0.25m);
        }

        AddRule(drawing, 48, 54, 499, 0.7m);
        AddText(drawing, $"IRIS : {branding.AddressLine1}, {branding.AddressLine2.Split(',')[0]},", 48, 42, 7);
        AddText(drawing, string.Join(",", branding.AddressLine2.Split(',').Skip(1)).Trim(), 48, 32, 7);
        AddText(drawing, $"MF : {branding.TaxIdentifier}", 48, 18, 7);
        AddText(drawing, $"RC : {branding.CommercialRegister}", 260, 18, 7);
        AddText(drawing, "Page 1", 510, 18, 7);

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

    private static void AddText(StringBuilder drawing, string? value, int x, int y, int size, bool bold = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var normalized = value.Replace('\u2019', '\'').Replace('\u2013', '-').Replace('\u2014', '-');
        var escaped = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (ch is '(' or ')' or '\\') escaped.Append('\\');
            escaped.Append(ch <= 255 ? ch : '?');
        }
        drawing.Append("BT /F").Append(bold ? '2' : '1').Append(' ').Append(size)
            .Append(" Tf 1 0 0 1 ").Append(x).Append(' ').Append(y).Append(" Tm (")
            .Append(escaped).Append(") Tj ET\n");
    }

    private static void AddRule(StringBuilder drawing, int x, int y, int width, decimal lineWidth) =>
        drawing.Append("0.45 0.55 0.62 RG ").Append(lineWidth.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(" w ").Append(x).Append(' ').Append(y).Append(" m ").Append(x + width).Append(' ').Append(y).Append(" l S\n");

    private static void DrawRect(StringBuilder drawing, int x, int y, int width, int height) =>
        drawing.Append("0.35 0.43 0.50 RG 0.75 w ").Append(x).Append(' ').Append(y).Append(' ')
            .Append(width).Append(' ').Append(height).Append(" re S\n");

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

    private static string JoinNonEmpty(string? first, string? second) =>
        string.Join(", ", new[] { first, second }.Where(value => !string.IsNullOrWhiteSpace(value)));

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
    private sealed record IrisInvoiceBankAccount(string? BankName, string? CurrencyCode, string? Iban, string? SwiftBic);
}
