using AutoMapper;
using IRISBackend.DTOs;
using IRISBackend.Models;
using IRISBackend.Repositories;
using System.Security.Claims;

namespace IRISBackend.Services
{
    public class CounterpartyService : ICounterpartyService
    {
        private readonly ICounterpartyRepository _counterpartyRepository;
        private readonly IGenericRepository<DocumentAttachment> _documentAttachmentRepository;
        private readonly ICounterpartyAttachmentStorageService _attachmentStorageService;
        private readonly IMapper _mapper;
        private readonly IHttpContextAccessor _httpContextAccessor;

        private const string CounterpartyDocumentCategory = "Counterparty";
        private const string CommercialRegisterDocumentType = "CommercialRegister";
        private const string TaxIdentificationNumberDocumentType = "TaxIdentificationNumber";
        private const string LocalStorageProvider = "LocalFileSystem";

        public CounterpartyService(
            ICounterpartyRepository counterpartyRepository,
            IGenericRepository<DocumentAttachment> documentAttachmentRepository,
            ICounterpartyAttachmentStorageService attachmentStorageService,
            IMapper mapper,
            IHttpContextAccessor httpContextAccessor)
        {
            _counterpartyRepository = counterpartyRepository;
            _documentAttachmentRepository = documentAttachmentRepository;
            _attachmentStorageService = attachmentStorageService;
            _mapper = mapper;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<ServiceResult<CounterpartyDetailsDto>> CreateAsync(CounterpartyCreateDto request, CancellationToken cancellationToken = default)
        {
            var uniqueValidation = await ValidateUniqueLegacyIdentifiersAsync(request.LegacyClientId, request.LegacyCompanyCode, null, cancellationToken);
            if (!uniqueValidation.Succeeded)
            {
                return uniqueValidation;
            }

            var counterparty = _mapper.Map<Counterparty>(request);
            counterparty.LegacyCompanyCode = NormalizeOptionalCode(request.LegacyCompanyCode);
            counterparty.Status = NormalizeStatus(request.Status);
            counterparty.IsActive = IsActiveStatus(counterparty.Status);
            counterparty.ContactName = null;
            counterparty.CommercialRegister = null;
            counterparty.TaxIdentificationNumber = null;
            counterparty.CreatedAtUtc = DateTime.UtcNow;

            await _counterpartyRepository.AddAsync(counterparty, cancellationToken);
            await _counterpartyRepository.SaveChangesAsync(cancellationToken);

            var userId = GetCurrentUserId();
            var attachmentResult = await UpsertAttachmentsAsync(
                counterparty.Id,
                request.CommercialRegister,
                request.TaxIdAttachment,
                userId,
                cancellationToken);

            if (!attachmentResult.Succeeded)
            {
                return ServiceResult<CounterpartyDetailsDto>.Failure(
                    attachmentResult.Error!.Message,
                    attachmentResult.Error.Errors,
                    attachmentResult.StatusCode);
            }

            var details = await BuildDetailsDtoAsync(counterparty, cancellationToken);
            return ServiceResult<CounterpartyDetailsDto>.Success(details, StatusCodes.Status201Created);
        }

        public async Task<ServiceResult<CounterpartyDetailsDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            var counterparty = await _counterpartyRepository.GetByIdAsync(id, cancellationToken);
            if (counterparty is null)
            {
                return NotFound();
            }

            var details = await BuildDetailsDtoAsync(counterparty, cancellationToken);
            return ServiceResult<CounterpartyDetailsDto>.Success(details);
        }

        public async Task<ServiceResult<PagedResponseDto<CounterpartyListItemDto>>> ListAsync(CounterpartyQueryDto query, CancellationToken cancellationToken = default)
        {
            var pageNumber = Math.Max(query.PageNumber, 1);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);
            var normalizedQuery = new CounterpartyQueryDto
            {
                PageNumber = pageNumber,
                PageSize = pageSize,
                Search = query.Search,
                Type = query.Type,
                Status = query.Status,
                IsActive = query.IsActive,
                Country = query.Country,
                City = query.City,
                SortBy = query.SortBy,
                SortDirection = query.SortDirection
            };

            var (items, totalCount) = await _counterpartyRepository.ListAsync(normalizedQuery, cancellationToken);

            return ServiceResult<PagedResponseDto<CounterpartyListItemDto>>.Success(new PagedResponseDto<CounterpartyListItemDto>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        }

        public async Task<ServiceResult<CounterpartyDetailsDto>> UpdateAsync(int id, CounterpartyUpdateDto request, CancellationToken cancellationToken = default)
        {
            var counterparty = await _counterpartyRepository.GetByIdAsync(id, cancellationToken);
            if (counterparty is null)
            {
                return NotFound();
            }

            var uniqueValidation = await ValidateUniqueLegacyIdentifiersAsync(request.LegacyClientId, request.LegacyCompanyCode, id, cancellationToken);
            if (!uniqueValidation.Succeeded)
            {
                return uniqueValidation;
            }

            _mapper.Map(request, counterparty);
            counterparty.LegacyCompanyCode = NormalizeOptionalCode(request.LegacyCompanyCode);
            counterparty.Status = NormalizeStatus(request.Status);
            counterparty.IsActive = IsActiveStatus(counterparty.Status);
            counterparty.ContactName = null;
            counterparty.CommercialRegister = null;
            counterparty.TaxIdentificationNumber = null;
            counterparty.UpdatedAtUtc = DateTime.UtcNow;

            _counterpartyRepository.Update(counterparty);
            await _counterpartyRepository.SaveChangesAsync(cancellationToken);

            var userId = GetCurrentUserId();
            var attachmentResult = await UpsertAttachmentsAsync(
                counterparty.Id,
                request.CommercialRegister,
                request.TaxIdAttachment,
                userId,
                cancellationToken);

            if (!attachmentResult.Succeeded)
            {
                return ServiceResult<CounterpartyDetailsDto>.Failure(
                    attachmentResult.Error!.Message,
                    attachmentResult.Error.Errors,
                    attachmentResult.StatusCode);
            }

            var details = await BuildDetailsDtoAsync(counterparty, cancellationToken);
            return ServiceResult<CounterpartyDetailsDto>.Success(details);
        }

        public async Task<ServiceResult<CounterpartyDetailsDto>> SetActiveStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default)
        {
            var counterparty = await _counterpartyRepository.GetByIdAsync(id, cancellationToken);
            if (counterparty is null)
            {
                return NotFound();
            }

            counterparty.IsActive = isActive;
            counterparty.Status = isActive ? "Active" : "Inactive";
            counterparty.UpdatedAtUtc = DateTime.UtcNow;

            _counterpartyRepository.Update(counterparty);
            await _counterpartyRepository.SaveChangesAsync(cancellationToken);

            var details = await BuildDetailsDtoAsync(counterparty, cancellationToken);
            return ServiceResult<CounterpartyDetailsDto>.Success(details);
        }

        private static ServiceResult<CounterpartyDetailsDto> NotFound()
        {
            return ServiceResult<CounterpartyDetailsDto>.Failure(
                "Counterparty was not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        private async Task<ServiceResult<CounterpartyDetailsDto>> ValidateUniqueLegacyIdentifiersAsync(
            int? legacyClientId,
            string? legacyCompanyCode,
            int? excludedId,
            CancellationToken cancellationToken)
        {
            if (legacyClientId.HasValue &&
                await _counterpartyRepository.LegacyClientIdExistsAsync(legacyClientId.Value, excludedId, cancellationToken))
            {
                return ServiceResult<CounterpartyDetailsDto>.Failure(
                    "Counterparty legacy client ID already exists.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (!string.IsNullOrWhiteSpace(legacyCompanyCode) &&
                await _counterpartyRepository.LegacyCompanyCodeExistsAsync(legacyCompanyCode, excludedId, cancellationToken))
            {
                return ServiceResult<CounterpartyDetailsDto>.Failure(
                    "Counterparty legacy company code already exists.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            return ServiceResult<CounterpartyDetailsDto>.Success(new CounterpartyDetailsDto());
        }

        private static string? NormalizeOptionalCode(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
        }

        private static string NormalizeStatus(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Active" : value.Trim();
        }

        private static bool IsActiveStatus(string status)
        {
            return !status.Equals("Inactive", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<CounterpartyDetailsDto> BuildDetailsDtoAsync(Counterparty counterparty, CancellationToken cancellationToken)
        {
            var details = _mapper.Map<CounterpartyDetailsDto>(counterparty);
            var attachments = await _documentAttachmentRepository.FindAsync(
                document => document.IdCounterparty == counterparty.Id &&
                    (document.DocumentType == CommercialRegisterDocumentType || document.DocumentType == TaxIdentificationNumberDocumentType),
                cancellationToken);

            var commercialRegisterAttachment = attachments
                .Where(document => document.DocumentType == CommercialRegisterDocumentType)
                .OrderByDescending(document => document.UpdatedAt ?? document.CreatedAt)
                .ThenByDescending(document => document.IdDocument)
                .FirstOrDefault();

            var taxIdentificationAttachment = attachments
                .Where(document => document.DocumentType == TaxIdentificationNumberDocumentType)
                .OrderByDescending(document => document.UpdatedAt ?? document.CreatedAt)
                .ThenByDescending(document => document.IdDocument)
                .FirstOrDefault();

            details.CommercialRegisterAttachment = MapAttachment(commercialRegisterAttachment);
            details.TaxIdentificationNumberAttachment = MapAttachment(taxIdentificationAttachment);
            return details;
        }

        private async Task<ServiceResult<bool>> UpsertAttachmentsAsync(
            int counterpartyId,
            AttachmentUploadDto? commercialRegisterAttachment,
            AttachmentUploadDto? taxIdentificationAttachment,
            string? userId,
            CancellationToken cancellationToken)
        {
            if (commercialRegisterAttachment is null && taxIdentificationAttachment is null)
            {
                return ServiceResult<bool>.Success(true);
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                return ServiceResult<bool>.Failure(
                    "User identity was not found in the token.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var existingAttachments = await _documentAttachmentRepository.FindAsync(
                document => document.IdCounterparty == counterpartyId &&
                    (document.DocumentType == CommercialRegisterDocumentType || document.DocumentType == TaxIdentificationNumberDocumentType),
                cancellationToken);

            var attachmentsByType = existingAttachments
                .GroupBy(document => document.DocumentType)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(document => document.UpdatedAt ?? document.CreatedAt).ThenByDescending(document => document.IdDocument).First());

            var now = DateTime.UtcNow;
            var storedPathsToDeleteOnFailure = new List<string>();
            var replacedPathsToDeleteOnSuccess = new List<string>();

            try
            {
                if (commercialRegisterAttachment is not null)
                {
                    await UpsertAttachmentAsync(
                        counterpartyId,
                        CommercialRegisterDocumentType,
                        commercialRegisterAttachment,
                        attachmentsByType.GetValueOrDefault(CommercialRegisterDocumentType),
                        userId,
                        now,
                        storedPathsToDeleteOnFailure,
                        replacedPathsToDeleteOnSuccess,
                        cancellationToken);
                }

                if (taxIdentificationAttachment is not null)
                {
                    await UpsertAttachmentAsync(
                        counterpartyId,
                        TaxIdentificationNumberDocumentType,
                        taxIdentificationAttachment,
                        attachmentsByType.GetValueOrDefault(TaxIdentificationNumberDocumentType),
                        userId,
                        now,
                        storedPathsToDeleteOnFailure,
                        replacedPathsToDeleteOnSuccess,
                        cancellationToken);
                }

                await _documentAttachmentRepository.SaveChangesAsync(cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                await CleanupStoredFilesAsync(storedPathsToDeleteOnFailure, cancellationToken);
                return ServiceResult<bool>.Failure(exception.Message);
            }
            catch (Exception)
            {
                await CleanupStoredFilesAsync(storedPathsToDeleteOnFailure, cancellationToken);
                return ServiceResult<bool>.Failure(
                    "The attachment metadata could not be saved.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            await CleanupStoredFilesAsync(replacedPathsToDeleteOnSuccess, cancellationToken);
            return ServiceResult<bool>.Success(true);
        }

        private async Task UpsertAttachmentAsync(
            int counterpartyId,
            string documentType,
            AttachmentUploadDto file,
            DocumentAttachment? existingAttachment,
            string userId,
            DateTime now,
            ICollection<string> storedPathsToDeleteOnFailure,
            ICollection<string> replacedPathsToDeleteOnSuccess,
            CancellationToken cancellationToken)
        {
            var storedAttachment = await _attachmentStorageService.SaveAsync(counterpartyId, file, cancellationToken);
            storedPathsToDeleteOnFailure.Add(storedAttachment.RelativePath);

            if (existingAttachment is null)
            {
                var newAttachment = new DocumentAttachment
                {
                    IdCounterparty = counterpartyId,
                    DocumentType = documentType,
                    DocumentCategory = CounterpartyDocumentCategory,
                    Title = ResolveTitle(documentType),
                    FileName = storedAttachment.StoredFileName,
                    OriginalFileName = storedAttachment.OriginalFileName,
                    FileExtension = storedAttachment.FileExtension,
                    MimeType = storedAttachment.MimeType,
                    FileSize = storedAttachment.FileSize,
                    StorageProvider = LocalStorageProvider,
                    FilePath = storedAttachment.RelativePath,
                    Status = "Active",
                    IsActive = true,
                    CreatedAt = now,
                    CreatedBy = userId
                };

                await _documentAttachmentRepository.AddAsync(newAttachment, cancellationToken);
                return;
            }

            if (!string.IsNullOrWhiteSpace(existingAttachment.FilePath))
            {
                replacedPathsToDeleteOnSuccess.Add(existingAttachment.FilePath);
            }

            existingAttachment.Title = ResolveTitle(documentType);
            existingAttachment.Description = null;
            existingAttachment.FileName = storedAttachment.StoredFileName;
            existingAttachment.OriginalFileName = storedAttachment.OriginalFileName;
            existingAttachment.FileExtension = storedAttachment.FileExtension;
            existingAttachment.MimeType = storedAttachment.MimeType;
            existingAttachment.FileSize = storedAttachment.FileSize;
            existingAttachment.StorageProvider = LocalStorageProvider;
            existingAttachment.FilePath = storedAttachment.RelativePath;
            existingAttachment.NoteContent = null;
            existingAttachment.ConfidentialityLevel = null;
            existingAttachment.Status = "Active";
            existingAttachment.IsActive = true;
            existingAttachment.UpdatedAt = now;
            existingAttachment.UpdatedBy = userId;

            _documentAttachmentRepository.Update(existingAttachment);
        }

        private string? GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        private static CounterpartyAttachmentDto? MapAttachment(DocumentAttachment? document)
        {
            if (document is null)
            {
                return null;
            }

            return new CounterpartyAttachmentDto
            {
                IdDocument = document.IdDocument,
                DocumentType = document.DocumentType,
                OriginalFileName = document.OriginalFileName,
                MimeType = document.MimeType,
                FileSize = document.FileSize,
                UploadedAt = document.UpdatedAt ?? document.CreatedAt,
                Status = document.Status,
                IsActive = document.IsActive
            };
        }

        private static string ResolveTitle(string documentType)
        {
            return documentType switch
            {
                CommercialRegisterDocumentType => "Commercial Register",
                TaxIdentificationNumberDocumentType => "Tax Identification Number",
                _ => documentType
            };
        }

        private async Task CleanupStoredFilesAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
        {
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await _attachmentStorageService.DeleteAsync(path, cancellationToken);
            }
        }
    }
}
