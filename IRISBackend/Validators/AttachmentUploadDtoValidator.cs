using FluentValidation;
using IRISBackend.DTOs;

namespace IRISBackend.Validators
{
    public class AttachmentUploadDtoValidator : AbstractValidator<AttachmentUploadDto?>
    {
        public AttachmentUploadDtoValidator()
        {
            RuleFor(attachment => attachment!.FileName)
                .NotEmpty()
                .MaximumLength(260)
                .When(attachment => attachment is not null);

            RuleFor(attachment => attachment!.ContentType)
                .NotEmpty()
                .MaximumLength(255)
                .When(attachment => attachment is not null);

            RuleFor(attachment => attachment!.Base64Content)
                .NotEmpty()
                .When(attachment => attachment is not null);
        }
    }
}
