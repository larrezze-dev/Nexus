using Shouldly;
using Xunit;

namespace Nexus.BuildingBlocks.Domain.Tests;

public sealed class ResultTests
{
    private static readonly Error SampleError = new("CAT-R2", "Something went wrong.");

    [Fact]
    public void Success_Produces_A_Successful_Result_Without_An_Error()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_Produces_A_Failed_Result_Carrying_The_Error()
    {
        var result = Result.Failure(SampleError);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SampleError);
    }

    [Fact]
    public void Generic_Success_Exposes_The_Value()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Generic_Failure_Carries_The_Error_And_No_Usable_Value()
    {
        var result = Result.Failure<int>(SampleError);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SampleError);
    }

    [Fact]
    public void Accessing_Value_On_A_Failed_Result_Throws()
    {
        var result = Result.Failure<int>(SampleError);

        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Failure_Without_An_Error_Throws()
    {
        Should.Throw<ArgumentNullException>(() => Result.Failure(null!));
    }
}
