namespace ImmunizationSystem.IntegrationTests;

public sealed class ApiSurfaceTests
{
    [Fact]
    public void Program_Type_Is_Public_For_WebApplicationFactory()
    {
        Assert.Equal("Program", typeof(Program).Name);
    }
}
