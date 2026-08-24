using IRISBackend.DTOs;
using Microsoft.Extensions.Options;
using System.Text;

namespace IRISBackend.Services
{
    public class CounterpartyAttachmentStorageService : ICounterpartyAttachmentStorageService
    {
        private static readonly Dictionary<string, string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg"
        };

        private readonly CounterpartyAttachmentStorageOptions _options;
        private readonly string _storageRootPath;
        private readonly HashSet<string> _allowedExtensions;

        public CounterpartyAttachmentStorageService(
            IOptions<CounterpartyAttachmentStorageOptions> options,
            IWebHostEnvironment environment)
        {
            _options = options.Value;
            _allowedExtensions = _options.AllowedExtensions
                .Where(extension => !string.IsNullOrWhiteSpace(extension))
                .Select(NormalizeExtension)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (_allowedExtensions.Count == 0)
            {
                _allowedExtensions = [".pdf", ".png", ".jpg", ".jpeg"];
            }

            if (_options.MaxFileSizeBytes <= 0)
            {
                _options.MaxFileSizeBytes = CounterpartyAttachmentStorageOptions.DefaultMaxFileSizeBytes;
            }

            var configuredRoot = string.IsNullOrWhiteSpace(_options.RootPath)
                ? Path.Combine("App_Data", "Uploads")
                : _options.RootPath;

            _storageRootPath = Path.GetFullPath(Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(environment.ContentRootPath, configuredRoot));
        }

        public async Task<StoredCounterpartyAttachment> SaveAsync(int counterpartyId, AttachmentUploadDto attachment, CancellationToken cancellationToken = default)
        {
            if (attachment is null)
            {
                throw new ArgumentNullException(nameof(attachment));
            }

            var originalFileName = SanitizeOriginalFileName(attachment.FileName);
            var declaredExtension = NormalizeExtension(Path.GetExtension(originalFileName));
            if (string.IsNullOrWhiteSpace(declaredExtension) || !_allowedExtensions.Contains(declaredExtension))
            {
                throw new InvalidOperationException($"Only the following file formats are allowed: {string.Join(", ", _allowedExtensions)}.");
            }

            var base64Content = ExtractBase64Payload(attachment.Base64Content);
            if (string.IsNullOrWhiteSpace(base64Content))
            {
                throw new InvalidOperationException("Attachment content cannot be empty.");
            }

            byte[] fileBytes;
            try
            {
                fileBytes = Convert.FromBase64String(base64Content);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("Attachment content is not valid Base64.");
            }

            if (fileBytes.Length == 0)
            {
                throw new InvalidOperationException("Attachment file cannot be empty.");
            }

            if (fileBytes.LongLength > _options.MaxFileSizeBytes)
            {
                throw new InvalidOperationException($"Attachment file size must not exceed {ToMegabytes(_options.MaxFileSizeBytes)} MB.");
            }

            var detectedFileType = DetectFileType(fileBytes);
            if (detectedFileType is null || !_allowedExtensions.Contains(detectedFileType.Extension))
            {
                throw new InvalidOperationException($"Only the following file formats are allowed: {string.Join(", ", _allowedExtensions)}.");
            }

            if (!ExtensionsMatch(declaredExtension, detectedFileType.Extension))
            {
                throw new InvalidOperationException("Attachment file extension does not match the uploaded file content.");
            }

            if (!string.IsNullOrWhiteSpace(attachment.ContentType) &&
                !string.Equals(attachment.ContentType.Trim(), detectedFileType.MimeType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Attachment content type does not match the uploaded file content.");
            }

            var storedFileName = $"{Guid.NewGuid():N}{detectedFileType.Extension}";
            var relativePath = Path.Combine("counterparties", counterpartyId.ToString(), storedFileName)
                .Replace('\\', '/');

            var destinationPath = ResolveFullPath(relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath)
                ?? throw new InvalidOperationException("Attachment directory could not be resolved.");

            Directory.CreateDirectory(destinationDirectory);

            await using var outputStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await outputStream.WriteAsync(fileBytes, cancellationToken);

            return new StoredCounterpartyAttachment
            {
                OriginalFileName = originalFileName,
                StoredFileName = storedFileName,
                RelativePath = relativePath,
                FileExtension = detectedFileType.Extension.TrimStart('.'),
                MimeType = detectedFileType.MimeType,
                FileSize = fileBytes.LongLength
            };
        }

        public Task DeleteAsync(string? relativePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return Task.CompletedTask;
            }

            var fullPath = ResolveFullPath(relativePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return Task.CompletedTask;
        }

        private string ResolveFullPath(string relativePath)
        {
            var normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(_storageRootPath, normalizedRelativePath));
            if (!fullPath.StartsWith(_storageRootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid attachment path.");
            }

            return fullPath;
        }

        private static string NormalizeExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return string.Empty;
            }

            return extension.StartsWith('.')
                ? extension.Trim().ToLowerInvariant()
                : $".{extension.Trim().ToLowerInvariant()}";
        }

        private static string SanitizeOriginalFileName(string? fileName)
        {
            var sanitized = Path.GetFileName(fileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                throw new InvalidOperationException("Attachment file name is invalid.");
            }

            var invalidCharacters = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(sanitized.Length);
            foreach (var character in sanitized)
            {
                builder.Append(invalidCharacters.Contains(character) ? '_' : character);
            }

            sanitized = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                throw new InvalidOperationException("Attachment file name is invalid.");
            }

            return sanitized;
        }

        private static long ToMegabytes(long sizeInBytes)
        {
            return sizeInBytes / (1024 * 1024);
        }

        private static bool ExtensionsMatch(string declaredExtension, string detectedExtension)
        {
            if (string.Equals(declaredExtension, detectedExtension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return (string.Equals(declaredExtension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(detectedExtension, ".jpeg", StringComparison.OrdinalIgnoreCase)) ||
                   (string.Equals(declaredExtension, ".jpeg", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(detectedExtension, ".jpg", StringComparison.OrdinalIgnoreCase));
        }

        private static string ExtractBase64Payload(string? content)
        {
            var trimmed = content?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var separatorIndex = trimmed.IndexOf(',');
                if (separatorIndex < 0 || separatorIndex == trimmed.Length - 1)
                {
                    return string.Empty;
                }

                return trimmed[(separatorIndex + 1)..].Trim();
            }

            return trimmed;
        }

        private static DetectedFileType? DetectFileType(byte[] content)
        {
            if (content.Length >= 4 &&
                content[0] == 0x25 &&
                content[1] == 0x50 &&
                content[2] == 0x44 &&
                content[3] == 0x46)
            {
                return new DetectedFileType(".pdf", AllowedMimeTypes[".pdf"]);
            }

            if (content.Length >= 8 &&
                content[0] == 0x89 &&
                content[1] == 0x50 &&
                content[2] == 0x4E &&
                content[3] == 0x47 &&
                content[4] == 0x0D &&
                content[5] == 0x0A &&
                content[6] == 0x1A &&
                content[7] == 0x0A)
            {
                return new DetectedFileType(".png", AllowedMimeTypes[".png"]);
            }

            if (content.Length >= 3 &&
                content[0] == 0xFF &&
                content[1] == 0xD8 &&
                content[2] == 0xFF)
            {
                return new DetectedFileType(".jpeg", AllowedMimeTypes[".jpg"]);
            }

            return null;
        }

        private sealed record DetectedFileType(string Extension, string MimeType);
    }
}
