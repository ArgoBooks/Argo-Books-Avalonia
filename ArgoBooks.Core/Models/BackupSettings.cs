namespace ArgoBooks.Core.Models;

/// <summary>
/// How often a copy of a company is kept, and where.
///
/// Stored globally rather than inside a company file on purpose: a setting that only exists inside
/// the file it protects is gone exactly when it is needed, and someone who turns backups on expects
/// it to hold for the next company they make as well.
/// </summary>
public class BackupSettings
{
    /// <summary>Off until the user asks for it. Nothing is written anywhere while this is false.</summary>
    public bool Enabled { get; set; }

    public BackupFrequency Frequency { get; set; } = BackupFrequency.Daily;

    /// <summary>
    /// Older copies are deleted once a newer one is safely written. Bounded on purpose: with a copy
    /// on every close an unbounded setting grows until the disk is full, and the person who picks it
    /// is the least likely to notice.
    /// </summary>
    public int CopiesToKeep { get; set; } = 5;

    /// <summary>An extra copy before an import or a restore, which are the changes people want to undo.</summary>
    public bool BeforeImports { get; set; } = true;

    /// <summary>Null until the user chooses one, so the default can follow whichever company is open.</summary>
    public string? Folder { get; set; }

    /// <summary>When the last copy was written, which is what <see cref="BackupFrequency"/> is measured from.</summary>
    public DateTime? LastBackupUtc { get; set; }
}

public enum BackupFrequency
{
    OnSave = 0,
    Daily,
    Weekly
}
