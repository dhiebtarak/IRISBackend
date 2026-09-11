using IRISBackend.DTOs;
using IRISBackend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Xunit;

namespace IRISBackend.Tests;

public sealed class DocumentStorageServiceTests
{
    [Fact]
    public async Task SaveAsync_StoresGeneratedFileAndCanReadIt()
    {
        var root = CreateRoot();
        try
        {
            var service = CreateService(root);
            var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\ncontent");

            var stored = await service.SaveAsync("contracts", 12, new DocumentUploadRequestDto
            {
                FileName = "signed-slip.pdf",
                MimeType = "application/pdf",
                ContentBase64 = Convert.ToBase64String(bytes)
            });

            Assert.Equal("signed-slip.pdf", stored.OriginalFileName);
            Assert.NotEqual(stored.OriginalFileName, stored.StoredFileName);
            Assert.Equal("contracts/12/" + stored.StoredFileName, stored.RelativePath);
            Assert.DoesNotContain(root, stored.RelativePath);

            await using (var stream = await service.OpenReadAsync(stored.RelativePath))
            {
                using var memory = new MemoryStream();
                await stream!.CopyToAsync(memory);
                Assert.Equal(bytes, memory.ToArray());
            }

            await service.DeleteAsync(stored.RelativePath);
            Assert.Null(await service.OpenReadAsync(stored.RelativePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("not-base64", "Document content is not valid Base64.")]
    [InlineData("", "Document content cannot be empty.")]
    public async Task SaveAsync_RejectsInvalidContent(string content, string expectedMessage)
    {
        var root = CreateRoot();
        try
        {
            var service = CreateService(root);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(
                "contracts", 1, new DocumentUploadRequestDto
                {
                    FileName = "document.pdf",
                    MimeType = "application/pdf",
                    ContentBase64 = content
                }));

            Assert.Equal(expectedMessage, exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_RejectsUnsupportedExtension()
    {
        var root = CreateRoot();
        try
        {
            var service = CreateService(root);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(
                "contracts", 1, new DocumentUploadRequestDto
                {
                    FileName = "script.exe",
                    MimeType = "application/octet-stream",
                    ContentBase64 = Convert.ToBase64String([1, 2, 3, 4, 5])
                }));

            Assert.Contains("file formats are allowed", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_RejectsOversizedContent()
    {
        var root = CreateRoot();
        try
        {
            var service = CreateService(root, maxFileSizeBytes: 4);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(
                "contracts", 1, new DocumentUploadRequestDto
                {
                    FileName = "document.pdf",
                    MimeType = "application/pdf",
                    ContentBase64 = Convert.ToBase64String([0x25, 0x50, 0x44, 0x46, 0x2D])
                }));

            Assert.Contains("must not exceed", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_RejectsPathTraversalInOriginalName()
    {
        var root = CreateRoot();
        try
        {
            var service = CreateService(root);
            var stored = await service.SaveAsync("contracts", 1, new DocumentUploadRequestDto
            {
                FileName = "..\\safe.pdf",
                MimeType = "application/pdf",
                ContentBase64 = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7"))
            });

            Assert.Equal("safe.pdf", stored.OriginalFileName);
            Assert.StartsWith("contracts/1/", stored.RelativePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static DocumentStorageService CreateService(string root, long maxFileSizeBytes = 10 * 1024 * 1024)
    {
        var options = Options.Create(new DocumentStorageOptions
        {
            RootPath = root,
            MaxFileSizeBytes = maxFileSizeBytes
        });

        return new DocumentStorageService(options, new TestHostEnvironment(root));
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "iris-document-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public TestHostEnvironment(string root)
        {
            ContentRootPath = root;
            ContentRootFileProvider = new PhysicalFileProvider(root);
        }

        public string ApplicationName { get; set; } = "IRISBackend.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
