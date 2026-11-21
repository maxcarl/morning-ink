using System.Security.Cryptography;
using System.Text;

namespace MorningInk.Infrastructure.Identity;

/// <summary>
/// One-time code for creating the first admin. It is written to the log at start while no user exists, so only
/// someone with access to the container can claim the instance, not whoever finds the URL first.
/// </summary>
public sealed class SetupCode
{
    // No 0/O or 1/I/L: the code is typed in from a log.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int Length = 16;
    private const int GroupLength = 4;

    private readonly Lock _lock = new();
    private string? _code;

    /// <summary>Creates a fresh code, e.g. "K7QM-3XPA-R9TD-W2HN", replacing any earlier one.</summary>
    public string Generate()
    {
        var code = RandomNumberGenerator.GetString(Alphabet, Length);
        lock (_lock)
        {
            _code = code;
        }

        return string.Join('-', code.Chunk(GroupLength).Select(group => new string(group)));
    }

    /// <summary>Whether <paramref name="input"/> is the current code; dashes, spaces and case do not matter.</summary>
    public bool Matches(string? input)
    {
        string? code;
        lock (_lock)
        {
            code = _code;
        }

        if (code is null || input is null)
        {
            return false;
        }

        var normalized = new string(input.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        // Constant time, so the comparison does not reveal how many characters were right.
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(normalized), Encoding.ASCII.GetBytes(code));
    }

    /// <summary>Makes the code unusable, once the first admin exists.</summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _code = null;
        }
    }
}
