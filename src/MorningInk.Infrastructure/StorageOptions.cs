namespace MorningInk.Infrastructure;

/// <summary>Where Morning Ink keeps its files. Configured in the "Storage" section.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    private const string DatabaseFileName = "morning-ink.db";
    private const string BackupDirectoryName = "backups";
    private const string KeysDirectoryName = "keys";
    private const string ImageDirectoryName = "images";
    private const string IssueDirectoryName = "issues";

    /// <summary>Directory for the database and everything else Morning Ink stores.</summary>
    public string DataDirectory { get; set; } = "/data";

    /// <summary><see cref="DataDirectory"/> as an absolute path; relative paths resolve against the working directory.</summary>
    public string DataDirectoryPath => Path.GetFullPath(DataDirectory);

    /// <summary>SQLite database file.</summary>
    public string DatabasePath => Path.Combine(DataDirectoryPath, DatabaseFileName);

    /// <summary>Database backups taken before migrations.</summary>
    public string BackupDirectory => Path.Combine(DataDirectoryPath, BackupDirectoryName);

    /// <summary>Data Protection keys. Without them, stored secrets cannot be decrypted.</summary>
    public string KeysDirectory => Path.Combine(DataDirectoryPath, KeysDirectoryName);

    /// <summary>Converted article images, named by the SHA-256 of their content.</summary>
    public string ImageDirectory => Path.Combine(DataDirectoryPath, ImageDirectoryName);

    /// <summary>Rendered issues, named by issue id.</summary>
    public string IssueDirectory => Path.Combine(DataDirectoryPath, IssueDirectoryName);
}
