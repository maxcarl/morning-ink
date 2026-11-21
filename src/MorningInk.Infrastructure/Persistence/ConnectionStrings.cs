using Microsoft.Data.Sqlite;

namespace MorningInk.Infrastructure.Persistence;

internal static class ConnectionStrings
{
    /// <summary>Builds a SQLite connection string; the builder escapes paths containing ";" or quotes.</summary>
    public static string ForFile(string path) => new SqliteConnectionStringBuilder { DataSource = path }.ToString();
}
