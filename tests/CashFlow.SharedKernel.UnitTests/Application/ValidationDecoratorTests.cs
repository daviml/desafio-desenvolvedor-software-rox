using CashFlow.SharedKernel.Application;
using CashFlow.SharedKernel.Results;
using FluentValidation;

namespace CashFlow.SharedKernel.UnitTests.Application;

public sealed class ValidationDecoratorTests
{
    private sealed record SampleCommand(string Name, int Quantity) : ICommand<string>;

    private sealed class SampleValidator : AbstractValidator<SampleCommand>
    {
        public SampleValidator()
        {
            RuleFor(command => command.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(command => command.Quantity).GreaterThan(0).WithMessage("Quantity must be positive.");
        }
    }

    private sealed class SpyHandler : IRequestHandler<SampleCommand, string>
    {
        public int Invocations { get; private set; }

        public Task<Result<string>> HandleAsync(SampleCommand request, CancellationToken cancellationToken)
        {
            Invocations++;
            return Task.FromResult(Result.Success("handled"));
        }
    }

    [Fact]
    public async Task HandleAsync_WithoutValidators_CallsTheInnerHandler()
    {
        var inner = new SpyHandler();
        var decorator = new ValidationDecorator<SampleCommand, string>(inner, []);

        var result = await decorator.HandleAsync(new SampleCommand("ok", 1), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        inner.Invocations.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_WithValidRequest_CallsTheInnerHandler()
    {
        var inner = new SpyHandler();
        var decorator = new ValidationDecorator<SampleCommand, string>(inner, [new SampleValidator()]);

        var result = await decorator.HandleAsync(new SampleCommand("ok", 1), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        inner.Invocations.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidRequest_ShortCircuitsWithFieldLevelDetails()
    {
        var inner = new SpyHandler();
        var decorator = new ValidationDecorator<SampleCommand, string>(inner, [new SampleValidator()]);

        var result = await decorator.HandleAsync(new SampleCommand("", 0), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Details.ShouldContainKey("Name");
        result.Error.Details.ShouldContainKey("Quantity");
        inner.Invocations.ShouldBe(0);
    }
}
