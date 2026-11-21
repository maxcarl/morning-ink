using Microsoft.Data.Sqlite;

namespace MorningInk.Infrastructure.Persistence;

internal static class ConnectionStrings
{
    public static string ForFile(string path) => new SqliteConnectionStringBuilder { DataSource = path }.ToString();
}
