using System.Text;

namespace IRISBackend.Services;

internal static class OutgoingClosingPdf
{
    public static byte[] Build(IEnumerable<string> lines)
    {
        var wrapped = new List<string>();
        foreach (var line in lines)
        {
            var remaining = line.Replace('\r', ' ').Replace('\n', ' ');
            while (remaining.Length > 95)
            {
                var split = remaining.LastIndexOf(' ', 94, 95);
                if (split <= 0) split = 95;
                wrapped.Add(remaining[..split]);
                remaining = remaining[split..].TrimStart();
            }
            wrapped.Add(remaining);
        }
        var pages = wrapped.Chunk(40).ToArray();
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, pages.Length).Select(i => $"{4 + i * 2} 0 R"))}] /Count {pages.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"
        };
        static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        for (var i = 0; i < pages.Length; i++)
        {
            var content = new StringBuilder("BT /F1 9 Tf 40 790 Td\n");
            foreach (var line in pages[i]) content.Append('(').Append(Escape(line)).Append(") Tj\n0 -18 Td\n");
            content.Append($"(Page {i + 1} of {pages.Length}) Tj\nET");
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + i * 2} 0 R >>");
            objects.Add($"<< /Length {Encoding.Latin1.GetByteCount(content.ToString())} >>\nstream\n{content}\nendstream");
        }
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(pdf.ToString()));
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var start = Encoding.Latin1.GetByteCount(pdf.ToString());
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:0000000000} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{start}\n%%EOF");
        return Encoding.Latin1.GetBytes(pdf.ToString());
    }
}
