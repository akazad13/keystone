using FluentValidation;

namespace Keystone.CoreApi.Validators;

// No OwnerId: the owner is always the caller, taken from the access token.
public record CreateProjectRequest(string Name, string? Description, DateOnly? DueDate);

public record UpdateProjectRequest(string Name, string? Description, string Status, DateOnly? DueDate);

public class CreateProjectValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MaximumLength(200).WithMessage("Project name must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.");
    }
}

public class UpdateProjectValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MaximumLength(200).WithMessage("Project name must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description must not exceed 2000 characters.");

        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("Status is required.")
            .Must(s => s is "Active" or "Archived" or "Completed")
            .WithMessage("Status must be Active, Archived, or Completed.");
    }
}
