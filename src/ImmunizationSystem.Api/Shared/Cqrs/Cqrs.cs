namespace ImmunizationSystem.Api.Shared.Cqrs;

public interface ICommand<TResult>
{
}

public interface IQuery<TResult>
{
}

public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

public interface IRequestDispatcher
{
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}

public sealed class RequestDispatcher(IServiceProvider serviceProvider) : IRequestDispatcher
{
    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        dynamic handler = (dynamic)serviceProvider.GetRequiredService(handlerType);
        return handler.HandleAsync((dynamic)command, cancellationToken);
    }

    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
        dynamic handler = (dynamic)serviceProvider.GetRequiredService(handlerType);
        return handler.HandleAsync((dynamic)query, cancellationToken);
    }
}

public static class HandlerRegistration
{
    public static IServiceCollection AddFeatureHandlers(this IServiceCollection services)
    {
        var assembly = typeof(HandlerRegistration).Assembly;
        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var serviceType in type.GetInterfaces().Where(i =>
                         i.IsGenericType &&
                         (i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
                          i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))))
            {
                services.AddScoped(serviceType, type);
            }
        }

        return services;
    }
}
