using CashFlow.Launches.Domain.Entries;
using FluentValidation;

namespace CashFlow.Launches.Application.Entries.RegisterEntry;

/// <summary>
/// Shape validation: cheap, request-scoped rules that let malformed input fail fast with a 400
/// before any I/O happens. Business invariants stay in the aggregate, not here.
/// </summary>
public sealed class RegisterEntryCommandValidator : AbstractValidator<RegisterEntryCommand>
{
    public RegisterEntryCommandValidator()
    {
        RuleFor(command => command.MerchantId)
            .NotEmpty()
            .WithMessage("MerchantId is required.");

        RuleFor(command => command.Type)
            .IsInEnum()
            .WithMessage("Type must be either Credit or Debit.");

        RuleFor(command => command.Amount)
            .GreaterThan(0m)
            .WithMessage("Amount must be greater than zero.")
            .LessThanOrEqualTo(1_000_000_000m)
            .WithMessage("Amount exceeds the maximum accepted value.")
            .Must(HaveAtMostTwoDecimalPlaces)
            .WithMessage("Amount must not have more than two decimal places.");

        RuleFor(command => command.Currency)
            .NotEmpty()
            .Length(3)
            .WithMessage("Currency must be a three-letter ISO-4217 code.");

        RuleFor(command => command.EntryDate)
            .NotEqual(default(DateOnly))
            .WithMessage("EntryDate is required.");

        RuleFor(command => command.Description)
            .NotEmpty()
            .WithMessage("Description is required.")
            .MaximumLength(Entry.MaxDescriptionLength);

        RuleFor(command => command.Category)
            .MaximumLength(Entry.MaxCategoryLength)
            .When(command => command.Category is not null);

        RuleFor(command => command.IdempotencyKey)
            .MaximumLength(Entry.MaxIdempotencyKeyLength)
            .When(command => command.IdempotencyKey is not null);
    }

    private static bool HaveAtMostTwoDecimalPlaces(decimal amount) =>
        decimal.Round(amount, 2) == amount;
}
