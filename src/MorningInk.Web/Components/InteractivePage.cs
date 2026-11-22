using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MorningInk.Domain;

namespace MorningInk.Web.Components;

/// <summary>Base for interactive pages: the signed-in user, a DI scope per action, and cancellation when the tab closes.</summary>
/// <remarks>Interactive pages have no HttpContext, so the user comes from the authentication state of the circuit.</remarks>
public abstract class InteractivePage : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _closed = new();

    protected Guid UserId { get; private set; }

    protected bool IsAdmin { get; private set; }

    protected string UserName { get; private set; } = string.Empty;

    /// <summary>Cancelled when the page goes away, so a slow feed test stops with it.</summary>
    protected CancellationToken Closed => _closed.Token;

    [Inject]
    private ActionScopes Scopes { get; set; } = default!; // ! is safe: the renderer injects it before any lifecycle method

    [Inject]
    private AuthenticationStateProvider Authentication { get; set; } = default!; // ! is safe: injected before any lifecycle method

    public void Dispose()
    {
        _closed.Cancel();
        _closed.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Loads the page's data once the user is known.</summary>
    protected abstract Task LoadAsync();

    protected sealed override async Task OnInitializedAsync()
    {
        var user = (await Authentication.GetAuthenticationStateAsync()).User;
        UserId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Pages require a signed-in user."));
        IsAdmin = user.IsInRole(nameof(UserRole.Admin));
        UserName = user.Identity?.Name ?? string.Empty;
        await LoadAsync();
    }

    /// <summary>Runs one action with a fresh instance of the service, see <see cref="ActionScopes"/>.</summary>
    protected Task<T> UseAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull => Scopes.RunAsync(action);

    protected Task UseAsync<TService>(Func<TService, Task> action)
        where TService : notnull => Scopes.RunAsync<TService, bool>(async service =>
        {
            await action(service);
            return true;
        });
}
