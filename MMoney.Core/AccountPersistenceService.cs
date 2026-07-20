using System.IO.Abstractions;

namespace MMoney.Core;

/// <summary>
/// Saves and loads <see cref="Account"/> state using per-account newline-delimited JSON event log files
/// stored in a single directory.
/// </summary>
/// <remarks>
/// Each account's event log is named after its <see cref="Account.Id"/> with no extension.
/// Deleted accounts are renamed with a <c>.deleted</c> extension and are excluded from <see cref="LoadAccounts"/>.
/// </remarks>
public sealed class AccountPersistenceService(string path, IFileSystem fileSystem)
{
    /// <summary>How many pre-restore backups to keep per account before pruning the oldest.</summary>
    private const int BackupRetention = 3;

    /// <summary>
    /// Loads all non-deleted accounts from the storage directory by replaying their event logs.
    /// </summary>
    /// <param name="ignoreMonthClosed">
    /// When <see langword="true"/>, <see cref="AccountEvent.MonthClosed"/> events are ignored
    /// so that closed months reappear in each account's transaction list.
    /// </param>
    /// <returns>A lazily-evaluated sequence of fully-replayed <see cref="Account"/> instances.</returns>
    public IEnumerable<Account> LoadAccounts(bool ignoreMonthClosed) => fileSystem.Directory.GetFiles(path)
        .Where(fileName => !IsDeleted(fileName))
        .Select(fileName => LoadAccount(ignoreMonthClosed, fileName));

    /// <summary>
    /// Returns the id and name of all deleted accounts in the storage directory.
    /// </summary>
    public IEnumerable<(Guid Id, string Name)> GetDeletedAccounts() =>
        fileSystem.Directory.GetFiles(path)
            .Where(IsDeleted)
            .Select(fileName =>
            {
                var id = Guid.Parse(fileSystem.Path.GetFileNameWithoutExtension(fileName)); // strips .deleted suffix
                var events = ReadEvents(fileName);
                var name = events.OfType<AccountEvent.NameSet>().LastOrDefault()?.Name ?? "(unnamed)";
                return (id, name);
            });

    /// <summary>
    /// Restores a previously deleted account by removing the <c>.deleted</c> suffix from its file.
    /// </summary>
    public Account RestoreAccount(Guid id, bool ignoreMonthClosed)
    {
        var deletedFileName = fileSystem.Path.Combine(path, id.ToString("N") + ".deleted");
        var restoredFileName = fileSystem.Path.Combine(path, id.ToString("N"));
        fileSystem.File.Move(deletedFileName, restoredFileName);
        return LoadAccount(ignoreMonthClosed, restoredFileName);
    }

    /// <summary>
    /// Marks an account as deleted by renaming its event log file with a <c>.deleted</c> suffix.
    /// </summary>
    /// <param name="account">The account to mark as deleted.</param>
    public void MarkAccountAsDeleted(Account account)
    {
        var fileName = MakeFileName(account);
        var deletedFileName = fileName + ".deleted";
        fileSystem.File.Move(fileName, deletedFileName);
    }

    /// <summary>Appends a single event to the account's event log file.</summary>
    /// <param name="account">The account that produced the event.</param>
    /// <param name="e">The event to persist.</param>
    public void Save(Account account, AccountEvent e)
    {
        var fileName = MakeFileName(account);
        fileSystem.File.AppendAllLines(fileName, AccountEventCodec.Encode([e]));
    }

    /// <summary>Reads an account's raw event-log lines, for backup/export.</summary>
    public IEnumerable<string> ReadRawLog(Account account) =>
        fileSystem.File.ReadLines(MakeFileName(account));

    /// <summary>
    /// Performs an admin restore by replacing an account's event log from <paramref name="lines"/>. 
    /// The lines are validated up-front by decoding and replaying them into a throwaway <see cref="Account"/>,
    /// so a malformed or unreplayable log throws and leaves the existing file untouched. On success
    /// the current log is copied to a timestamped <c>.bak-*</c> and atomically replaced; the freshly
    /// replayed account is returned.
    /// </summary>
    public Account ReplaceLog(Account account, IEnumerable<string> lines, bool ignoreMonthClosed)
    {
        // Validate before touching disk. Decoding throws on a malformed line and the Account
        // constructor throws if the events do not replay correctly.
        var events = AccountEventCodec.Decode(lines).ToList();
        var reloaded = new Account(account.Id, events, ignoreMonthClosed);
        var fileName = MakeFileName(account);

        // Backups (and the temp used during the swap) live in a subdirectory so LoadAccounts,
        // which scans only the top-level directory, never mistakes them for accounts.
        var backupsDir = fileSystem.Path.Combine(path, "backups");
        fileSystem.Directory.CreateDirectory(backupsDir);

        if (fileSystem.File.Exists(fileName))
        {
            fileSystem.File.Copy(fileName, NextBackupPath(backupsDir, account.Id), overwrite: false);
            PruneBackups(backupsDir, account.Id);
        }

        var temp = fileSystem.Path.Combine(backupsDir, $"{account.Id:N}.import");
        fileSystem.File.WriteAllLines(temp, AccountEventCodec.Encode(events));
        if (fileSystem.File.Exists(fileName))
        {
            fileSystem.File.Delete(fileName);
        }

        fileSystem.File.Move(temp, fileName);

        return reloaded;
    }

    /// <summary>
    /// Creates a new account file under <paramref name="id"/> from an exported log — an admin import that adopts the
    /// backup's identity (restoring onto a fresh install / different account). Validates first by decoding and
    /// replaying into an <see cref="Account"/>, so a malformed or unreplayable log throws and writes nothing; also
    /// throws if an account already exists at that id. Returns the freshly replayed account.
    /// </summary>
    public Account CreateFromLog(Guid id, IEnumerable<string> lines, bool ignoreMonthClosed)
    {
        var events = AccountEventCodec.Decode(lines).ToList();
        var account = new Account(id, events, ignoreMonthClosed);

        var fileName = MakeFileName(account);
        if (fileSystem.File.Exists(fileName))
        {
            throw new InvalidOperationException("An account already exists at this id.");
        }

        fileSystem.File.WriteAllLines(fileName, AccountEventCodec.Encode(events));
        return account;
    }

    // A unique backup path: timestamp to the millisecond, with a numeric suffix only if two restores
    // land in the same millisecond, so rapid restores never clobber each other.
    private string NextBackupPath(string backupsDir, Guid id)
    {
        var baseName = $"{id:N}.{DateTime.Now:yyyyMMddHHmmssfff}";
        var candidate = fileSystem.Path.Combine(backupsDir, baseName);
        var n = 1;
        while (fileSystem.File.Exists(candidate))
        {
            candidate = fileSystem.Path.Combine(backupsDir, $"{baseName}-{n++}");
        }

        return candidate;
    }

    // Keep only the newest BackupRetention backups for this account (the timestamped names sort
    // chronologically, so ordering by name is ordering by age).
    private void PruneBackups(string backupsDir, Guid id)
    {
        var prefix = id.ToString("N");
        var stale = fileSystem.Directory.GetFiles(backupsDir)
            .Where(file => fileSystem.Path.GetFileName(file).StartsWith(prefix))
            .OrderByDescending(file => file)
            .Skip(BackupRetention);
        foreach (var file in stale)
        {
            fileSystem.File.Delete(file);
        }
    }

    private Account LoadAccount(bool ignoreMonthClosed, string fileName)
    {
        var id = GetId(fileName);
        var events = ReadEvents(fileName);
        return new Account(id, events, ignoreMonthClosed);
    }

    private IEnumerable<AccountEvent> ReadEvents(string fileName) =>
        AccountEventCodec.Decode(fileSystem.File.ReadLines(fileName));

    private string MakeFileName(Account account) => fileSystem.Path.Combine(path, account.Id.ToString("N"));

    private static bool IsDeleted(string fileName) => fileName.EndsWith(".deleted");

    private Guid GetId(string fileName) => Guid.Parse(fileSystem.Path.GetFileNameWithoutExtension(fileName));
}