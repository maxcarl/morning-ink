namespace MorningInk.Web;

/// <summary>Runs one action of an interactive page in a DI scope of its own.</summary>
/// <remarks>
/// An interactive page lives as long as its browser tab. Scoped services injected into it would keep one DbContext for all
/// that time, with a growing change tracker and errors when two clicks overlap. A scope per action avoids both.
/// </remarks>
public sealed class ActionScopes(IServiceScopeFactory scopes)
{
    public async Task<T> RunAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
