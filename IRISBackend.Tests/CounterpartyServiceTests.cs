using AutoMapper;
using IRISBackend.DTOs;
using IRISBackend.Mappings;
using IRISBackend.Models;
using IRISBackend.Repositories;
using IRISBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text;
using SecurityClaim = System.Security.Claims.Claim;
using Xunit;

namespace IRISBackend.Tests
{
    public class CounterpartyServiceTests
    {
        [Fact]
        public async Task CreateAsync_WhenLegacyIdentifiersAreUnique_CreatesActiveCounterparty()
        {
            var repository = new FakeCounterpartyRepository();
            var service = CreateService(repository);

            var result = await service.CreateAsync(new CounterpartyCreateDto
            {
                LegacyClientId = 1001,
                LegacyCompanyCode = " cp-a ",
                Name = "Partner Re",
                Type = "Reinsurer",
                Email = "contact@example.com",
                Status = "Active"
            });

            Assert.True(result.Succeeded);
            Assert.Equal(201, result.StatusCode);
            Assert.Equal(1001, result.Value!.LegacyClientId);
            Assert.Equal("CP-A", result.Value.LegacyCompanyCode);
            Assert.Equal("Active", result.Value.Status);
            Assert.True(result.Value.IsActive);
            Assert.Single(repository.Items);
        }

        [Fact]
        public async Task CreateAsync_WhenLegacyClientIdExists_ReturnsConflict()
        {
            var repository = new FakeCounterpartyRepository();
            repository.Items.Add(new Counterparty
            {
                Id = 7,
                LegacyClientId = 1001,
                Name = "Existing",
                Type = "Broker",
                Status = "Active"
            });

            var service = CreateService(repository);

            var result = await service.CreateAsync(new CounterpartyCreateDto
            {
                LegacyClientId = 1001,
                Name = "Duplicate",
                Type = "Broker",
                Status = "Active"
            });

            Assert.False(result.Succeeded);
            Assert.Equal(409, result.StatusCode);
            Assert.Equal("Counterparty legacy client ID already exists.", result.Error!.Message);
            Assert.Single(repository.Items);
        }

        [Fact]
        public async Task CreateAsync_WhenAttachmentsAreProvided_StoresThemInDocumentAttachmentTable()
        {
            var repository = new FakeCounterpartyRepository();
            var documentRepository = new FakeDocumentAttachmentRepository();
            var storageService = new FakeCounterpartyAttachmentStorageService();
            var service = CreateService(repository, documentRepository, storageService);

            var result = await service.CreateAsync(new CounterpartyCreateDto
            {
                Name = "Partner Re",
                Type = "Reinsurer",
                Status = "Active",
                CommercialRegister = CreateAttachmentUploadDto("commercial-register.pdf", "application/pdf", CreatePdfBytes(128), asDataUrl: true),
                TaxIdAttachment = CreateAttachmentUploadDto("tax-id.pdf", "application/pdf", CreatePdfBytes(256))
            });

            Assert.True(result.Succeeded);
            Assert.Equal(2, documentRepository.Items.Count);
            Assert.Contains(documentRepository.Items, attachment => attachment.DocumentType == "CommercialRegister");
            Assert.Contains(documentRepository.Items, attachment => attachment.DocumentType == "TaxIdentificationNumber");
            Assert.Contains(documentRepository.Items, attachment => attachment.FileSize == 128);
            Assert.Contains(documentRepository.Items, attachment => attachment.FileSize == 256);
            Assert.NotNull(result.Value!.CommercialRegisterAttachment);
            Assert.NotNull(result.Value.TaxIdentificationNumberAttachment);
        }

        [Fact]
        public async Task CreateAsync_WhenSingleAttachmentIsProvided_StoresOnlyThatAttachment()
        {
            var repository = new FakeCounterpartyRepository();
            var documentRepository = new FakeDocumentAttachmentRepository();
            var storageService = new FakeCounterpartyAttachmentStorageService();
            var service = CreateService(repository, documentRepository, storageService);

            var result = await service.CreateAsync(new CounterpartyCreateDto
            {
                Name = "Single Attachment Partner",
                Type = "Broker",
                Status = "Active",
                CommercialRegister = CreateAttachmentUploadDto("commercial-register.pdf", "application/pdf", CreatePdfBytes(64))
            });

            Assert.True(result.Succeeded);
            Assert.Single(documentRepository.Items);
            Assert.Equal("CommercialRegister", documentRepository.Items[0].DocumentType);
            Assert.NotNull(result.Value!.CommercialRegisterAttachment);
            Assert.Null(result.Value.TaxIdentificationNumberAttachment);
        }

        [Fact]
        public async Task UpdateAsync_WhenCounterpartyExists_UpdatesNewSchemaFields()
        {
            var repository = new FakeCounterpartyRepository();
            var documentRepository = new FakeDocumentAttachmentRepository();
            var storageService = new FakeCounterpartyAttachmentStorageService();
            repository.Items.Add(new Counterparty
            {
                Id = 1,
                LegacyClientId = 1001,
                LegacyCompanyCode = "CP-A",
                Name = "Old Name",
                Type = "Broker",
                Status = "Active",
                IsActive = true,
                ContactName = "Legacy Contact",
                CommercialRegister = "LEGACY-RC",
                TaxIdentificationNumber = "LEGACY-TAX"
            });

            var service = CreateService(repository, documentRepository, storageService);

            var result = await service.UpdateAsync(1, new CounterpartyUpdateDto
            {
                LegacyClientId = 1002,
                LegacyCompanyCode = "cp-b",
                Name = "New Name",
                Type = "Cedant",
                PostalCode = "20000",
                Website = "https://example.com",
                Activity = "Insurance",
                CommercialRegister = CreateAttachmentUploadDto("commercial-register.pdf", "application/pdf", CreatePdfBytes(512)),
                CedantLegalEntity = "Cedant SA",
                Observations = "Updated",
                TaxIdAttachment = CreateAttachmentUploadDto("tax-id.pdf", "application/pdf", CreatePdfBytes(1024)),
                Status = "Active"
            });

            Assert.True(result.Succeeded);
            Assert.Equal(1002, result.Value!.LegacyClientId);
            Assert.Equal("CP-B", result.Value.LegacyCompanyCode);
            Assert.Equal("New Name", repository.Items[0].Name);
            Assert.Equal("20000", repository.Items[0].PostalCode);
            Assert.Null(repository.Items[0].ContactName);
            Assert.Null(repository.Items[0].CommercialRegister);
            Assert.Null(repository.Items[0].TaxIdentificationNumber);
            Assert.Equal(2, documentRepository.Items.Count);
            Assert.NotNull(repository.Items[0].UpdatedAtUtc);
        }

        [Fact]
        public async Task SetActiveStatusAsync_WhenCounterpartyExists_UpdatesIsActiveAndStatusWithoutDeleting()
        {
            var repository = new FakeCounterpartyRepository();
            var documentRepository = new FakeDocumentAttachmentRepository();
            repository.Items.Add(new Counterparty
            {
                Id = 1,
                LegacyClientId = 1001,
                Name = "Counterparty",
                Type = "Broker",
                Status = "Active",
                IsActive = true
            });

            var service = CreateService(repository, documentRepository);

            var result = await service.SetActiveStatusAsync(1, false);

            Assert.True(result.Succeeded);
            Assert.False(result.Value!.IsActive);
            Assert.Equal("Inactive", result.Value.Status);
            Assert.Single(repository.Items);
        }

        [Fact]
        public async Task CreateAsync_WhenAttachmentBase64IsInvalid_ReturnsBadRequest()
        {
            var repository = new FakeCounterpartyRepository();
            var documentRepository = new FakeDocumentAttachmentRepository();
            var service = CreateService(repository, documentRepository);

            var result = await service.CreateAsync(new CounterpartyCreateDto
            {
                Name = "Counterparty",
                Type = "Broker",
                Status = "Active",
                CommercialRegister = new AttachmentUploadDto
                {
                    FileName = "commercial-register.pdf",
                    ContentType = "application/pdf",
                    Base64Content = "not-base64"
                }
            });

            Assert.False(result.Succeeded);
            Assert.Equal(400, result.StatusCode);
            Assert.Equal("Attachment content is not valid Base64.", result.Error!.Message);
        }

        [Fact]
        public async Task CreateAsync_WhenAttachmentSignatureIsUnsupported_ReturnsBadRequest()
        {
            var repository = new FakeCounterpartyRepository();
            var documentRepository = new FakeDocumentAttachmentRepository();
            var service = CreateService(repository, documentRepository);

            var result = await service.CreateAsync(new CounterpartyCreateDto
            {
                Name = "Counterparty",
                Type = "Broker",
                Status = "Active",
                CommercialRegister = CreateAttachmentUploadDto(
                    "commercial-register.pdf",
                    "application/pdf",
                    Encoding.UTF8.GetBytes("plain text content"))
            });

            Assert.False(result.Succeeded);
            Assert.Equal(400, result.StatusCode);
            Assert.Equal("Only the following file formats are allowed: .pdf, .png, .jpg, .jpeg.", result.Error!.Message);
        }

        [Fact]
        public async Task CreateAsync_WhenAttachmentIsOversized_ReturnsBadRequest()
        {
            var repository = new FakeCounterpartyRepository();
            var documentRepository = new FakeDocumentAttachmentRepository();
            var service = CreateService(repository, documentRepository);
            var oversizedBytes = CreatePdfBytes(10 * 1024 * 1024 + 1);

            var result = await service.CreateAsync(new CounterpartyCreateDto
            {
                Name = "Counterparty",
                Type = "Broker",
                Status = "Active",
                TaxIdAttachment = CreateAttachmentUploadDto("tax-id.pdf", "application/pdf", oversizedBytes)
            });

            Assert.False(result.Succeeded);
            Assert.Equal(400, result.StatusCode);
            Assert.Equal("Attachment file size must not exceed 10 MB.", result.Error!.Message);
        }

        private static CounterpartyService CreateService(
            ICounterpartyRepository repository,
            IGenericRepository<DocumentAttachment>? documentRepository = null,
            ICounterpartyAttachmentStorageService? storageService = null)
        {
            var mapperConfig = new MapperConfiguration(
                configuration => configuration.AddProfile<CounterpartyProfile>(),
                NullLoggerFactory.Instance);

            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new SecurityClaim(ClaimTypes.NameIdentifier, "user-1")
                    ], "Test"))
                }
            };

            return new CounterpartyService(
                repository,
                documentRepository ?? new FakeDocumentAttachmentRepository(),
                storageService ?? new FakeCounterpartyAttachmentStorageService(),
                mapperConfig.CreateMapper(),
                httpContextAccessor);
        }

        private static AttachmentUploadDto CreateAttachmentUploadDto(string fileName, string contentType, byte[] bytes, bool asDataUrl = false)
        {
            var base64 = Convert.ToBase64String(bytes);
            return new AttachmentUploadDto
            {
                FileName = fileName,
                ContentType = contentType,
                Base64Content = asDataUrl
                    ? $"data:{contentType};base64,{base64}"
                    : base64
            };
        }

        private static byte[] CreatePdfBytes(int targetLength)
        {
            var header = Encoding.ASCII.GetBytes("%PDF-1.4\n");
            if (targetLength <= header.Length)
            {
                return header;
            }

            var bytes = new byte[targetLength];
            Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
            for (var index = header.Length; index < bytes.Length; index++)
            {
                bytes[index] = 0x20;
            }

            return bytes;
        }

        private sealed class FakeCounterpartyRepository : ICounterpartyRepository
        {
            public List<Counterparty> Items { get; } = [];

            public IQueryable<Counterparty> Query()
            {
                return Items.AsQueryable();
            }

            public Task<Counterparty?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Items.FirstOrDefault(counterparty => counterparty.Id == (int)id));
            }

            public Task<IEnumerable<Counterparty>> FindAsync(Expression<Func<Counterparty, bool>> predicate, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Items.AsQueryable().Where(predicate).AsEnumerable());
            }

            public Task AddAsync(Counterparty entity, CancellationToken cancellationToken = default)
            {
                entity.Id = Items.Count == 0 ? 1 : Items.Max(counterparty => counterparty.Id) + 1;
                Items.Add(entity);
                return Task.CompletedTask;
            }

            public void Update(Counterparty entity)
            {
            }

            public void Remove(Counterparty entity)
            {
                Items.Remove(entity);
            }

            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(1);
            }

            public Task<bool> LegacyClientIdExistsAsync(int legacyClientId, int? excludedId = null, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Items.Any(counterparty =>
                    counterparty.LegacyClientId == legacyClientId &&
                    (!excludedId.HasValue || counterparty.Id != excludedId.Value)));
            }

            public Task<bool> LegacyCompanyCodeExistsAsync(string legacyCompanyCode, int? excludedId = null, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Items.Any(counterparty =>
                    counterparty.LegacyCompanyCode != null &&
                    counterparty.LegacyCompanyCode.Equals(legacyCompanyCode.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    (!excludedId.HasValue || counterparty.Id != excludedId.Value)));
            }

            public Task<(IReadOnlyList<CounterpartyListItemDto> Items, int TotalCount)> ListAsync(CounterpartyQueryDto query, CancellationToken cancellationToken = default)
            {
                IReadOnlyList<CounterpartyListItemDto> items = Items.Select(counterparty => new CounterpartyListItemDto
                {
                    Id = counterparty.Id,
                    LegacyClientId = counterparty.LegacyClientId,
                    LegacyCompanyCode = counterparty.LegacyCompanyCode,
                    Name = counterparty.Name,
                    Type = counterparty.Type,
                    Email = counterparty.Email,
                    Country = counterparty.Country,
                    City = counterparty.City,
                    Status = counterparty.Status,
                    IsActive = counterparty.IsActive
                }).ToList();
                return Task.FromResult((items, Items.Count));
            }

            public Task<(IReadOnlyList<CounterpartyLookupItemDto> Items, int TotalCount)> SearchActiveReinsurersAsync(
                string? search,
                int page,
                int pageSize,
                CancellationToken cancellationToken = default)
            {
                var normalizedSearch = search?.Trim();
                var query = Items.Where(counterparty =>
                    counterparty.IsActive &&
                    !string.IsNullOrWhiteSpace(counterparty.Type) &&
                    counterparty.Type.Trim().Equals("Reinsurer", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(normalizedSearch))
                {
                    query = query.Where(counterparty =>
                        counterparty.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(counterparty.Country) && counterparty.Country.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(counterparty.City) && counterparty.City.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)));
                }

                var filtered = query.ToList();
                IReadOnlyList<CounterpartyLookupItemDto> items = filtered
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(counterparty => new CounterpartyLookupItemDto
                    {
                        Id = counterparty.Id,
                        Name = counterparty.Name,
                        Type = counterparty.Type,
                        Country = counterparty.Country,
                        City = counterparty.City
                    })
                    .ToList();

                return Task.FromResult((items, filtered.Count));
            }
        }

        private sealed class FakeDocumentAttachmentRepository : IGenericRepository<DocumentAttachment>
        {
            public List<DocumentAttachment> Items { get; } = [];

            public IQueryable<DocumentAttachment> Query()
            {
                return Items.AsQueryable();
            }

            public Task<DocumentAttachment?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Items.FirstOrDefault(document => document.IdDocument == (int)id));
            }

            public Task<IEnumerable<DocumentAttachment>> FindAsync(Expression<Func<DocumentAttachment, bool>> predicate, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Items.AsQueryable().Where(predicate).AsEnumerable());
            }

            public Task AddAsync(DocumentAttachment entity, CancellationToken cancellationToken = default)
            {
                entity.IdDocument = Items.Count == 0 ? 1 : Items.Max(document => document.IdDocument) + 1;
                Items.Add(entity);
                return Task.CompletedTask;
            }

            public void Update(DocumentAttachment entity)
            {
            }

            public void Remove(DocumentAttachment entity)
            {
                Items.Remove(entity);
            }

            public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(1);
            }
        }

        private sealed class FakeCounterpartyAttachmentStorageService : ICounterpartyAttachmentStorageService
        {
            private readonly List<string> _deletedPaths = [];
            private const long MaxFileSizeBytes = 10 * 1024 * 1024;

            public Task<StoredCounterpartyAttachment> SaveAsync(int counterpartyId, AttachmentUploadDto attachment, CancellationToken cancellationToken = default)
            {
                var originalFileName = Path.GetFileName(attachment.FileName);
                var extension = Path.GetExtension(originalFileName).TrimStart('.').ToLowerInvariant();
                var storedFileName = $"{Guid.NewGuid():N}.{extension}";
                var relativePath = $"counterparties/{counterpartyId}/{storedFileName}";
                var base64Payload = attachment.Base64Content.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    ? attachment.Base64Content[(attachment.Base64Content.IndexOf(',') + 1)..]
                    : attachment.Base64Content;
                byte[] bytes;

                try
                {
                    bytes = Convert.FromBase64String(base64Payload);
                }
                catch (FormatException)
                {
                    throw new InvalidOperationException("Attachment content is not valid Base64.");
                }

                if (bytes.LongLength == 0)
                {
                    throw new InvalidOperationException("Attachment file cannot be empty.");
                }

                if (bytes.LongLength > MaxFileSizeBytes)
                {
                    throw new InvalidOperationException("Attachment file size must not exceed 10 MB.");
                }

                if (!HasSupportedSignature(bytes))
                {
                    throw new InvalidOperationException("Only the following file formats are allowed: .pdf, .png, .jpg, .jpeg.");
                }

                return Task.FromResult(new StoredCounterpartyAttachment
                {
                    OriginalFileName = originalFileName,
                    StoredFileName = storedFileName,
                    RelativePath = relativePath,
                    FileExtension = extension,
                    MimeType = ContentTypeOrDefault(attachment.ContentType, extension),
                    FileSize = bytes.LongLength
                });
            }

            public Task DeleteAsync(string? relativePath, CancellationToken cancellationToken = default)
            {
                if (!string.IsNullOrWhiteSpace(relativePath))
                {
                    _deletedPaths.Add(relativePath);
                }

                return Task.CompletedTask;
            }

            private static string ContentTypeOrDefault(string? contentType, string extension)
            {
                if (!string.IsNullOrWhiteSpace(contentType))
                {
                    return contentType;
                }

                return extension.Equals("pdf", StringComparison.OrdinalIgnoreCase)
                    ? "application/pdf"
                    : "image/jpeg";
            }

            private static bool HasSupportedSignature(byte[] content)
            {
                return
                    (content.Length >= 4 &&
                     content[0] == 0x25 &&
                     content[1] == 0x50 &&
                     content[2] == 0x44 &&
                     content[3] == 0x46) ||
                    (content.Length >= 8 &&
                     content[0] == 0x89 &&
                     content[1] == 0x50 &&
                     content[2] == 0x4E &&
                     content[3] == 0x47 &&
                     content[4] == 0x0D &&
                     content[5] == 0x0A &&
                     content[6] == 0x1A &&
                     content[7] == 0x0A) ||
                    (content.Length >= 3 &&
                     content[0] == 0xFF &&
                     content[1] == 0xD8 &&
                     content[2] == 0xFF);
            }
        }
    }
}
