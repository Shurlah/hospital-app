using ImmunizationSystem.Api.Shared.Phone;

namespace ImmunizationSystem.UnitTests;

public sealed class PhoneNumberFormatterTests
{
    [Theory]
    [InlineData("08012345678", "+2348012345678")]
    [InlineData("0801 234 5678", "+2348012345678")]
    [InlineData("080-1234-5678", "+2348012345678")]
    [InlineData("2348012345678", "+2348012345678")]
    [InlineData("+2348012345678", "+2348012345678")]
    [InlineData("8012345678", "+2348012345678")]
    public void TryNormalizeToNigerianE164_Normalizes_Valid_Numbers(string input, string expected)
    {
        var succeeded = PhoneNumberFormatter.TryNormalizeToNigerianE164(input, out var normalized);

        Assert.True(succeeded);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("123")]
    [InlineData("12345678901234")]
    public void TryNormalizeToNigerianE164_Rejects_Invalid_Numbers(string? input)
    {
        var succeeded = PhoneNumberFormatter.TryNormalizeToNigerianE164(input, out var normalized);

        Assert.False(succeeded);
        Assert.Equal(string.Empty, normalized);
    }
}
