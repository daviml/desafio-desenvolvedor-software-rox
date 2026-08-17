using CashFlow.SharedKernel.Results;

namespace CashFlow.SharedKernel.UnitTests.Results;

public sealed class ResultTests
{
    [Fact]
    public void Success_CarriesTheValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Failure_CarriesTheError()
    {
        var error = Error.NotFound("entry.not_found", "Entry was not found.");

        var result = Result.Failure<int>(error);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }

    [Fact]
    public void Value_OnAFailedResult_Throws()
    {
        var result = Result.Failure<int>(Error.Conflict("x", "y"));

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_ProducesSuccess()
    {
        Result<string> result = "ok";

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("ok");
    }

    [Fact]
    public void ImplicitConversion_FromError_ProducesFailure()
    {
        Result<string> result = Error.Validation("bad", "Bad request.");

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Validation_WithDetails_KeepsFieldLevelMessages()
    {
        var details = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Amount"] = ["Amount must be greater than zero."],
        };

        var error = Error.Validation(details);

        error.Type.ShouldBe(ErrorType.Validation);
        error.Details["Amount"].ShouldHaveSingleItem();
    }
}
