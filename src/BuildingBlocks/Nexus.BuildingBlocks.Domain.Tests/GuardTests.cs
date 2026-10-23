using Shouldly;
using Xunit;

namespace Nexus.BuildingBlocks.Domain.Tests;

public sealed class GuardTests
{
    [Fact]
    public void Null_Throws_When_Value_Is_Null()
    {
        Should.Throw<ArgumentNullException>(() => Guard.Against.Null<string>(null, "value"));
    }

    [Fact]
    public void Null_Returns_The_Value_When_Not_Null()
    {
        var value = Guard.Against.Null("hello", "value");

        value.ShouldBe("hello");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrWhiteSpace_Throws_For_Blank_Values(string? value)
    {
        Should.Throw<ArgumentException>(() => Guard.Against.NullOrWhiteSpace(value, "value"));
    }

    [Fact]
    public void NullOrWhiteSpace_Returns_The_Value_When_Meaningful()
    {
        var value = Guard.Against.NullOrWhiteSpace("hello", "value");

        value.ShouldBe("hello");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NegativeOrZero_Throws_For_Zero_Or_Negative_Values(int value)
    {
        Should.Throw<ArgumentException>(() => Guard.Against.NegativeOrZero(value, "value"));
    }

    [Fact]
    public void NegativeOrZero_Returns_The_Value_When_Positive()
    {
        var value = Guard.Against.NegativeOrZero(5, "value");

        value.ShouldBe(5);
    }

    [Fact]
    public void Negative_Throws_For_Negative_Values()
    {
        Should.Throw<ArgumentException>(() => Guard.Against.Negative(-1, "value"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Negative_Returns_The_Value_When_Zero_Or_Positive(int value)
    {
        var result = Guard.Against.Negative(value, "value");

        result.ShouldBe(value);
    }
}
