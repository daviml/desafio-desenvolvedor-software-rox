using FluentValidation;

namespace CashFlow.Launches.Application.Entries.CancelEntry;

public sealed class CancelEntryCommandValidator : AbstractValidator<CancelEntryCommand>
{
    public const int MaxReasonLength = 300;

    public CancelEntryCommandValidator()
    {
        RuleFor(command => command.EntryId)
            .NotEmpty()
            .WithMessage("EntryId is required.");

        RuleFor(command => command.Reason)
            .MaximumLength(MaxReasonLength)
            .When(command => command.Reason is not null);
    }
}
