using System.Threading.RateLimiting;

namespace MorningInk.Web;

/// <summary>
/// Limits sign-in attempts per client address: the login and setup forms, and the OPDS catalog, which checks the
/// reader password on every request. Each check hashes on purpose slowly, so without a limit anyone could guess
/// passwords or keep the CPU busy without an account.
/// </summary>
/// <remarks>Behind a reverse proxy all clients share the proxy's address; the limit is generous enough for a household.</remarks>
public static class SignInLimits
{
    public const int AttemptsPerMinute = 20;

    public static IServiceCollection AddSignInLimits(this IServiceCollection services) =>
        services.AddRateLimiter(limits =>
        {
            // Partitions are cached by key: the unlimited one must never share a key with a client's.
            limits.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                IsSignIn(context.Request)
                    ? RateLimitPartition.GetTokenBucketLimiter($"client:{context.Connection.RemoteIpAddress}", _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = AttemptsPerMinute,
                        TokensPerPeriod = AttemptsPerMinute,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter("unlimited"));
            limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // A body of its own, so the status code pages do not turn it into "not found".
            limits.OnRejected = (rejected, cancellationToken) =>
                new ValueTask(rejected.HttpContext.Response.WriteAsync("Too many attempts. Wait a minute and try again.", cancellationToken));
        });

    private static bool IsSignIn(HttpRequest request) =>
        request.Path.StartsWithSegments("/opds")
        || (HttpMethods.IsPost(request.Method) && (request.Path.StartsWithSegments("/login") || request.Path.StartsWithSegments("/setup")));
}
