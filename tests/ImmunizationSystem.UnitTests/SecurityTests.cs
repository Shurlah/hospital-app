using ImmunizationSystem.Api.Shared.Security;

namespace ImmunizationSystem.UnitTests;

public sealed class SecurityTests
{
    [Fact]
    public void PasswordHasher_Verifies_Hashed_Password()
    {
        var hasher = new BCryptPasswordHasher();

        var hash = hasher.Hash("correct horse battery staple");

        Assert.True(hasher.Verify("correct horse battery staple", hash));
        Assert.False(hasher.Verify("wrong password", hash));
    }
}
