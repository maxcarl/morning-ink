namespace MorningInk.Infrastructure;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    private const string DatabaseFileName = "morning-ink.db";
    private const string BackupDirectoryName = "backups";

    /// <summary>Directory for the database and everything else Morning Ink stores.</summary>
    public string DataDirectory { get; set; } = "/data";

    public string DataDirectoryPath => Path.GetFullPath(DataDirectory);

    public string DatabasePath => Path.Combine(DataDirectoryPath, DatabaseFileName);

    public string BackupDirectory => Path.Combine(DataDirectoryPath, BackupDirectoryName);
}
