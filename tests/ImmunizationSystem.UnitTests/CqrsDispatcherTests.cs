using ImmunizationSystem.Api.Shared.Cqrs;
using Microsoft.Extensions.DependencyInjection;

namespace ImmunizationSystem.UnitTests;

public sealed class CqrsDispatcherTests
{
    [Fact]
    public async Task SendAsync_Resolves_Command_Handler()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<PingCommand, string>, PingHandler>();
        services.AddScoped<IRequestDispatcher, RequestDispatcher>();
        await using var provider = services.BuildServiceProvider();

        var dispatcher = provider.GetRequiredService<IRequestDispatcher>();

        var result = await dispatcher.SendAsync(new PingCommand("ok"));

        Assert.Equal("handled:ok", result);
    }

    public sealed record PingCommand(string Value) : ICommand<string>;

    public sealed class PingHandler : ICommandHandler<PingCommand, string>
    {
        public Task<string> HandleAsync(PingCommand command, CancellationToken cancellationToken)
            => Task.FromResult($"handled:{command.Value}");
    }
}
