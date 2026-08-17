using CashFlow.Launches.Application.Entries.RegisterEntry;
using CashFlow.Launches.Domain.Entries;

namespace CashFlow.Launches.UnitTests.Application;

public sealed class RegisterEntryCommandValidatorTests
{
    private readonly RegisterEntryCommandValidator _validator = new();

    private static RegisterEntryCommand ValidCommand() => new(
        Guid.NewGuid(),
        EntryType.Credit,
        10m,
        new DateOnly(2026, 3, 1),
        "Venda");

    [Fact]
    public void Validate_AcceptsAWellFormedCommand()
    {
        _validator.Validate(ValidCommand()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsAnEmptyMerchantId()
    {
        var result = _validator.Validate(ValidCommand() with { MerchantId = Guid.Empty });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == "MerchantId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsNonPositiveAmounts(decimal amount)
    {
        _validator.Validate(ValidCommand() with { Amount = amount }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_RejectsMoreThanTwoDecimalPlaces()
    {
        var result = _validator.Validate(ValidCommand() with { Amount = 10.999m });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.ErrorMessage.Contains("decimal places", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsAnUnknownEntryType()
    {
        _validator.Validate(ValidCommand() with { Type = (EntryType)99 }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_RejectsAMalformedCurrencyCode()
    {
        _validator.Validate(ValidCommand() with { Currency = "REAL" }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_RejectsAnEmptyDescription()
    {
        _validator.Validate(ValidCommand() with { Description = "" }).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_RejectsAnOverlongIdempotencyKey()
    {
        var command = ValidCommand() with
        {
            IdempotencyKey = new string('k', Entry.MaxIdempotencyKeyLength + 1),
        };

        _validator.Validate(command).IsValid.ShouldBeFalse();
    }
}
