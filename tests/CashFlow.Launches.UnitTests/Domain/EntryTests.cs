using CashFlow.Launches.Domain.Entries;
using CashFlow.Launches.Domain.Entries.Events;
using CashFlow.Launches.UnitTests.TestSupport;
using CashFlow.SharedKernel.Domain;

namespace CashFlow.Launches.UnitTests.Domain;

public sealed class EntryTests
{
    private static readonly MerchantId Merchant = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly FixedClock Clock = FixedClock.Default;
    private static readonly DateOnly Today = FixedClock.Default.Today;

    private static Entry RegisterCredit(decimal amount = 100m, DateOnly? date = null) =>
        Entry.Register(
            Merchant,
            EntryType.Credit,
            Money.From(amount),
            date ?? Today,
            "Venda balcão",
            Clock);

    [Fact]
    public void Register_CreatesAnActiveEntryAndRaisesTheDomainEvent()
    {
        var entry = RegisterCredit(150.75m);

        entry.MerchantId.ShouldBe(Merchant);
        entry.Type.ShouldBe(EntryType.Credit);
        entry.Amount.Amount.ShouldBe(150.75m);
        entry.Status.ShouldBe(EntryStatus.Active);
        entry.IsCancelled.ShouldBeFalse();
        entry.RegisteredAtUtc.ShouldBe(FixedClock.DefaultNow);
        entry.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<EntryRegisteredDomainEvent>();
    }

    [Fact]
    public void Register_TrimsTheDescriptionAndTheCategory()
    {
        var entry = Entry.Register(
            Merchant,
            EntryType.Debit,
            Money.From(10m),
            Today,
            "  Aluguel  ",
            Clock,
            category: "  Custos fixos  ");

        entry.Description.ShouldBe("Aluguel");
        entry.Category.ShouldBe("Custos fixos");
    }

    [Fact]
    public void Register_TurnsABlankCategoryIntoNull()
    {
        var entry = Entry.Register(Merchant, EntryType.Debit, Money.From(10m), Today, "Água", Clock, category: "   ");

        entry.Category.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Register_RejectsAmountsThatAreNotPositive(decimal amount)
    {
        var exception = Should.Throw<DomainException>(() => RegisterCredit(amount));

        exception.Code.ShouldBe("entry.amount_not_positive");
    }

    [Fact]
    public void Register_RejectsAFutureEntryDate()
    {
        var exception = Should.Throw<DomainException>(() => RegisterCredit(date: Today.AddDays(1)));

        exception.Code.ShouldBe("entry.date_in_future");
    }

    [Fact]
    public void Register_AcceptsToday()
    {
        Should.NotThrow(() => RegisterCredit(date: Today));
    }

    [Fact]
    public void Register_RejectsAnEntryDateBeyondTheBackdatingWindow()
    {
        var tooOld = Today.AddDays(-(Entry.MaxBackdatingDays + 1));

        var exception = Should.Throw<DomainException>(() => RegisterCredit(date: tooOld));

        exception.Code.ShouldBe("entry.date_too_old");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_RequiresADescription(string description)
    {
        var exception = Should.Throw<DomainException>(() =>
            Entry.Register(Merchant, EntryType.Credit, Money.From(10m), Today, description, Clock));

        exception.Code.ShouldBe("entry.description_required");
    }

    [Fact]
    public void Register_RejectsAnOverlongDescription()
    {
        var description = new string('a', Entry.MaxDescriptionLength + 1);

        var exception = Should.Throw<DomainException>(() =>
            Entry.Register(Merchant, EntryType.Credit, Money.From(10m), Today, description, Clock));

        exception.Code.ShouldBe("entry.description_too_long");
    }

    [Fact]
    public void SignedAmount_IsPositiveForCreditsAndNegativeForDebits()
    {
        var credit = Entry.Register(Merchant, EntryType.Credit, Money.From(40m), Today, "Venda", Clock);
        var debit = Entry.Register(Merchant, EntryType.Debit, Money.From(40m), Today, "Compra", Clock);

        credit.SignedAmount.Amount.ShouldBe(40m);
        debit.SignedAmount.Amount.ShouldBe(-40m);
    }

    [Fact]
    public void Cancel_MarksTheEntryAndRaisesTheDomainEvent()
    {
        var entry = RegisterCredit();
        entry.ClearDomainEvents();

        entry.Cancel("Lançamento duplicado", Clock);

        entry.Status.ShouldBe(EntryStatus.Cancelled);
        entry.IsCancelled.ShouldBeTrue();
        entry.CancelledAtUtc.ShouldBe(FixedClock.DefaultNow);
        entry.CancellationReason.ShouldBe("Lançamento duplicado");
        entry.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<EntryCancelledDomainEvent>();
    }

    [Fact]
    public void Cancel_KeepsTheOriginalAmountForCompensation()
    {
        var entry = RegisterCredit(99.99m);
        entry.ClearDomainEvents();

        entry.Cancel(null, Clock);

        var cancelled = entry.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<EntryCancelledDomainEvent>();
        cancelled.Amount.Amount.ShouldBe(99.99m);
        cancelled.Type.ShouldBe(EntryType.Credit);
        cancelled.Reason.ShouldBeNull();
    }

    [Fact]
    public void Cancel_Twice_IsRejected()
    {
        var entry = RegisterCredit();
        entry.Cancel("primeira", Clock);

        var exception = Should.Throw<DomainException>(() => entry.Cancel("segunda", Clock));

        exception.Code.ShouldBe("entry.already_cancelled");
    }

    [Fact]
    public void Entities_WithTheSameIdentity_AreEqual()
    {
        var entry = RegisterCredit();

        entry.Equals(entry).ShouldBeTrue();
        entry.Equals(RegisterCredit()).ShouldBeFalse();
    }
}
