using IRISBackend.DTOs;
using Microsoft.Extensions.Options;
using System.Text;

namespace IRISBackend.Services;

public sealed class DocumentStorageService : IDocumentStorageService
{
    private static readonly IReadOnlyDictionary<string, string> AllowedMimeTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".txt"] = "text/plain",
            [".csv"] = "text/csv"
        };

    private readonly DocumentStorageOptions _options;
    private readonly string _storageRootPath;
    private readonly HashSet<string> _allowedExtensions;

    public DocumentStorageService(
        IOptions<DocumentStorageOptions> options,
        IWebHostEnvironment environment)
    {
        _options = options.Value;
        _allowedExtensions = _options.AllowedExtensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(NormalizeExtension)
            .Where(AllowedMimeTypes.ContainsKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_allowedExtensions.Count == 0)
        {
            _allowedExtensions = AllowedMimeTypes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (_options.MaxFileSizeBytes <= 0)
        {
            _options.MaxFileSizeBytes = DocumentStorageOptions.DefaultMaxFileSizeBytes;
        }

        var configuredRoot = string.IsNullOrWhiteSpace(_options.RootPath)
            ? Path.Combine("App_Data", "Documents")
            : _options.RootPath;

        _storageRootPath = Path.GetFullPath(Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot));
    }

    public async Task<StoredDocument> SaveAsync(
        string scope,
        int ownerId,
        DocumentUploadRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new InvalidOperationException("Document upload request is required.");
        }

        var originalFileName = SanitizeOriginalFileName(request.FileName);
        var extension = NormalizeExtension(Path.GetExtension(originalFileName));
        if (!_allowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                $"Only the following file formats are allowed: {string.Join(", ", _allowedExtensions)}.");
        }

        if (!AllowedMimeTypes.TryGetValue(extension, out var expectedMimeType) ||
            !string.Equals(request.MimeType?.Trim(), expectedMimeType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Document MIME type does not match the file extension.");
        }

        var payload = ExtractBase64Payload(request.ContentBase64);
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("Document content cannot be empty.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Document content is not valid Base64.");
        }

        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("Document file cannot be empty.");
        }

        if (bytes.LongLength > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"Document file size must not exceed {_options.MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        if (!HasValidSignature(extension, bytes))
        {
            throw new InvalidOperationException("Document content does not match the declared file type.");
        }

        var safeScope = SanitizePathSegment(scope);
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var relativePath = Path.Combine(safeScope, ownerId.ToString(), storedFileName).Replace('\\', '/');
        var destinationPath = ResolveFullPath(relativePath);
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Document directory could not be resolved.");

        Directory.CreateDirectory(directory);
        await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await output.WriteAsync(bytes, cancellationToken);

        return new StoredDocument
        {
            OriginalFileName = originalFileName,
            StoredFileName = storedFileName,
            RelativePath = relativePath,
            FileExtension = extension.TrimStart('.'),
            MimeType = expectedMimeType,
            FileSize = bytes.LongLength
        };
    }

    public Task DeleteAsync(string? relativePath, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            var fullPath = ResolveFullPath(relativePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        return Task.CompletedTask;
    }

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(relativePath);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    private string ResolveFullPath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_storageRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = _storageRootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid document path.");
        }

        return fullPath;
    }

    private static string SanitizeOriginalFileName(string? fileName)
    {
        var sanitized = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new InvalidOperationException("Document file name is invalid.");
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(sanitized.Length);
        foreach (var character in sanitized)
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        return builder.ToString().Trim();
    }

    private static string SanitizePathSegment(string value)
    {
        var segment = value.Trim();
        if (string.IsNullOrWhiteSpace(segment) || segment.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid document storage scope.");
        }

        return segment;
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return string.Empty;
        return extension.StartsWith('.') ? extension.Trim().ToLowerInvariant() : $".{extension.Trim().ToLowerInvariant()}";
    }

    private static string ExtractBase64Payload(string? content)
    {
        var trimmed = content?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return string.Empty;
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return trimmed;
        var separator = trimmed.IndexOf(',');
        return separator >= 0 && separator < trimmed.Length - 1 ? trimmed[(separator + 1)..].Trim() : string.Empty;
    }

    private static bool HasValidSignature(string extension, byte[] content)
    {
        if (extension == ".pdf") return StartsWith(content, "%PDF-"u8.ToArray());
        if (extension == ".png") return StartsWith(content, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        if (extension is ".jpg" or ".jpeg") return content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF;
        if (extension is ".doc" or ".xls") return StartsWith(content, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);
        if (extension is ".docx" or ".xlsx") return StartsWith(content, [0x50, 0x4B, 0x03, 0x04]);
        return IsText(content);
    }

    private static bool StartsWith(byte[] value, byte[] prefix)
    {
        return value.Length >= prefix.Length && value.AsSpan(0, prefix.Length).SequenceEqual(prefix);
    }

    private static bool IsText(byte[] content)
    {
        if (content.Contains((byte)0)) return false;
        try
        {
            _ = Encoding.UTF8.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
