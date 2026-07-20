namespace MMoney.Core;

/// <summary>
/// Creates and manages a collection of <see cref="Account"/> instances, wiring each account
/// to the persistence layer so that events are saved automatically.
/// </summary>
/// <remarks>
/// If the storage directory contains no accounts, a default "New Account" is created automatically.
/// </remarks>
public sealed class AccountManager
{
    private readonly AccountPersistenceService persistenceService;
    private readonly TimeProvider timeProvider;
    private readonly List<Account> accounts;
    private bool ignoreMonthClosed;

    /// <summary>
    /// Creates an <see cref="AccountManager"/> by loading all accounts from <paramref name="persistenceService"/>.
    /// </summary>
    /// <param name="persistenceService">The service used to load and persist account events.</param>
    /// <param name="ignoreMonthClosed">
    /// When <see langword="true"/>, <see cref="AccountEvent.MonthClosed"/> events are ignored
    /// during replay so that closed months reappear.
    /// </param>
    /// <param name="timeProvider">
    /// The clock the orchestration layer owns and resolves the current date from, so that date-dependent
    /// account operations (closing a month, calculating an as-of balance) are fed a single, live "today"
    /// rather than the aggregate holding a clock of its own.
    /// </param>
    public AccountManager(AccountPersistenceService persistenceService, bool ignoreMonthClosed, TimeProvider timeProvider)
    {
        this.persistenceService = persistenceService;
        this.ignoreMonthClosed = ignoreMonthClosed;
        this.timeProvider = timeProvider;
        this.accounts = [];

        LoadAccounts();
    }

    /// <summary>The current local date, resolved live from the injected <see cref="TimeProvider"/>.</summary>
    public DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    /// <summary>Whether closed months are collapsed away on replay (<see langword="false"/>) or kept
    /// visible with only their date lock applied (<see langword="true"/>).</summary>
    public bool IgnoreMonthClosed => ignoreMonthClosed;

    /// <summary>Returns all currently loaded accounts.</summary>
    public IReadOnlyList<Account> GetAccounts() => accounts.AsReadOnly();

    /// <summary>
    /// Changes how <see cref="AccountEvent.MonthClosed"/> events replay and reloads every account from
    /// persistence so the change takes effect immediately. With <paramref name="value"/> true, closed
    /// months reappear (only their earliest-allowed-date lock is kept); with false, they are collapsed
    /// into a carried balance. A no-op when unchanged. Reloaded account instances replace the old ones.
    /// </summary>
    public void SetIgnoreMonthClosed(bool value)
    {
        if (value == ignoreMonthClosed)
        {
            return;
        }

        UntrackAll();
        ignoreMonthClosed = value;
        LoadAccounts();
    }

    /// <summary>Returns the id and display name of all deleted accounts.</summary>
    public IEnumerable<(Guid Id, string Name)> GetDeletedAccounts() =>
        persistenceService.GetDeletedAccounts();

    /// <summary>Restores a previously deleted account and makes it active.</summary>
    public Account RestoreAccount(Guid id)
    {
        ThrowIfNotDeleted(id);

        var account = persistenceService.RestoreAccount(id, ignoreMonthClosed);

        // Compute the unique name before tracking, so MakeUniqueName does not see the account conflict with itself.
        var uniqueName = MakeUniqueName(account.Name);
        Track(account);
        if (!string.Equals(uniqueName, account.Name, StringComparison.OrdinalIgnoreCase))
        {
            account.SetName(uniqueName);
        }

        return account;
    }

    /// <summary>Creates a new account with the given name and persists it.</summary>
    /// <param name="name">The display name for the account. Must not be null or whitespace.</param>
    /// <returns>The newly created <see cref="Account"/>.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is null or whitespace, or an account with the same name already exists.
    /// </exception>
    public Account AddAccount(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ThrowIfDuplicateName(name);

        var account = new Account(Guid.NewGuid(), []);
        Track(account);
        account.SetName(name);
        return account;
    }

    /// <summary>Removes an account from the manager and marks its event log as deleted.</summary>
    /// <param name="account">The account to remove.</param>
    /// <returns><see langword="true"/> if the account was found and removed; <see langword="false"/> if it was not managed by this instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="account"/> is <see langword="null"/>.</exception>
    public bool RemoveAccount(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!Untrack(account))
        {
            return false;
        }

        persistenceService.MarkAccountAsDeleted(account);
        return true;
    }

    /// <summary>Returns an account's raw event-log lines (an admin backup/export).</summary>
    public IEnumerable<string> ExportAccount(Account account) =>
        persistenceService.ReadRawLog(account);

    /// <summary>
    /// Validates an exported log and summarises it <em>without touching disk</em>, so an import can be confirmed
    /// against what it contains. Decodes the lines (throws on a malformed line) and replays them into a throwaway
    /// account (throws if they do not replay), then reports the event count, the account name, and the latest date
    /// any event touches. Discards the throwaway account.
    /// </summary>
    public ImportPreview PreviewImport(IEnumerable<string> lines)
    {
        var events = AccountEventCodec.Decode(lines).ToList();
        _ = new Account(Guid.NewGuid(), events, ignoreMonthClosed); // validates replay; the instance is discarded
        var name = events.OfType<AccountEvent.NameSet>().LastOrDefault()?.Name ?? "(unnamed)";
        return new ImportPreview(events.Count, name, LatestEventDate(events));
    }

    /// <summary>
    /// Replaces <paramref name="target"/>'s entire history from an exported event log as part of an admin restore,
    /// keeping <paramref name="target"/>'s identity. Equivalent to <see cref="ImportAccount(Account, Guid, IEnumerable{string})"/>
    /// with the backup id equal to the target's id (the same-identity path). Throws without changing anything if the
    /// log is invalid.
    /// </summary>
    public Account ImportAccount(Account target, IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ImportAccount(target, target.Id, lines);
    }

    /// <summary>
    /// Imports an exported log into the account it belongs to (<paramref name="backupId"/> — the id the export was
    /// named after). When <paramref name="backupId"/> equals <paramref name="target"/>'s id, it replaces the target's
    /// history in place (backing up the old log). When it differs, the import <em>adopts the backup's identity</em>:
    /// the account is created under <paramref name="backupId"/>, and <paramref name="target"/> is retired as a
    /// recoverable deleted account — this is what lets a backup be restored onto a fresh install whose default
    /// account has a different id. Validates before any change, so an invalid log throws and leaves everything
    /// untouched. Returns the reloaded/created account.
    /// </summary>
    public Account ImportAccount(Account target, Guid backupId, IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (backupId == target.Id)
        {
            var reloaded = persistenceService.ReplaceLog(target, lines, ignoreMonthClosed);

            var index = accounts.IndexOf(target);
            if (index >= 0)
            {
                Replace(index, target, reloaded); // in-place: keep the managed list position
            }
            else
            {
                Track(reloaded); // off-list target: adopt the reloaded account — leaving it unwired was the bug
            }

            return reloaded;
        }

        // Adopt the backup's identity: create the account under backupId (validates first, writes nothing on
        // failure), then retire the current account as a recoverable deleted account and swap the new one in.
        var created = persistenceService.CreateFromLog(backupId, lines, ignoreMonthClosed);
        persistenceService.MarkAccountAsDeleted(target);
        Untrack(target);
        Track(created);
        return created;
    }

    // The latest date any event touches, across every date-bearing field — a recency cue for the import summary.
    private static DateOnly? LatestEventDate(IReadOnlyList<AccountEvent> events)
    {
        DateOnly? latest = null;
        void Consider(DateOnly d)
        {
            if (latest is null || d > latest)
            {
                latest = d;
            }
        }

        foreach (var e in events)
        {
            switch (e)
            {
                case AccountEvent.TransactionAdded t: Consider(t.Date); break;
                case AccountEvent.TransactionAmountChanged t: Consider(t.Date); break;
                case AccountEvent.TransactionDateChanged t: Consider(t.Date); Consider(t.NewDate); break;
                case AccountEvent.TransactionDescriptionChanged t: Consider(t.Date); break;
                case AccountEvent.TransactionRemoved t: Consider(t.Date); break;
                case AccountEvent.MonthClosed t: Consider(t.Month.LastDay); break;
                case AccountEvent.SequenceRemoved t: Consider(t.Date); Consider(t.FromDate); break;
                case AccountEvent.SequenceAmountChanged t: Consider(t.Date); break;
                case AccountEvent.SequenceDescriptionChanged t: Consider(t.Date); break;
            }
        }

        return latest;
    }

    private void OnNewEvent(object? sender, AccountEvent e)
    {
        if (sender is Account account)
        {
            persistenceService.Save(account, e);
        }
    }

    // The only places an account's persistence subscription is touched. Each fuses the subscription to list
    // membership, so the invariant "a managed account (in `accounts`) is subscribed to Save exactly once, and an
    // unmanaged one is not" holds by construction — no caller maintains the pair by hand. No `NewEvent +=/-=`
    // appears outside these four.

    private void Track(Account account)
    {
        account.NewEvent += OnNewEvent;
        accounts.Add(account);
    }

    private bool Untrack(Account account)
    {
        if (!accounts.Remove(account))
        {
            return false;
        }

        account.NewEvent -= OnNewEvent;
        return true;
    }

    private void UntrackAll()
    {
        foreach (var account in accounts)
        {
            account.NewEvent -= OnNewEvent;
        }

        accounts.Clear();
    }

    // Swap a managed account for its reloaded replacement in place, keeping its list position.
    private void Replace(int index, Account old, Account fresh)
    {
        old.NewEvent -= OnNewEvent;
        fresh.NewEvent += OnNewEvent;
        accounts[index] = fresh;
    }

    private void ThrowIfDuplicateName(string name)
    {
        if (accounts.Any(account => string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Duplicate account name.");
        }
    }

    private void ThrowIfNotDeleted(Guid id)
    {
        if (accounts.Any(account => account.Id == id))
        {
            throw new InvalidOperationException("Account has not been deleted");
        }
    }

    private void LoadAccounts()
    {
        foreach (var account in persistenceService.LoadAccounts(ignoreMonthClosed))
        {
            Track(account);
        }

        if (accounts.Count == 0)
        {
            AddAccount("New Account");
        }
    }

    private string MakeUniqueName(string baseName)
    {
        if (!accounts.Any(a => string.Equals(a.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        var counter = 2;
        string candidate;
        do
        {
            candidate = $"{baseName} ({counter++})";
        }
        while (accounts.Any(a => string.Equals(a.Name, candidate, StringComparison.OrdinalIgnoreCase)));

        return candidate;
    }
}

/// <summary>
/// A validated summary of an export log an import is about to apply, for the confirm dialog — the log is not applied
/// to produce it. Carries the event count, the account name in the log, and the latest date any event touches.
/// </summary>
public sealed record ImportPreview(int EventCount, string Name, DateOnly? LatestDate);