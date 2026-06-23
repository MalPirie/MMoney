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

        foreach (var account in accounts)
        {
            account.NewEvent -= OnNewEvent;
        }

        ignoreMonthClosed = value;

        accounts.Clear();
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
        account.NewEvent += OnNewEvent;

        var uniqueName = MakeUniqueName(account.Name);
        if (!string.Equals(uniqueName, account.Name, StringComparison.OrdinalIgnoreCase))
        {
            account.SetName(uniqueName);
        }

        accounts.Add(account);

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
        account.NewEvent += OnNewEvent;
        accounts.Add(account);
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

        if (!accounts.Remove(account))
        {
            return false;
        }

        account.NewEvent -= OnNewEvent;
        persistenceService.MarkAccountAsDeleted(account);
        return true;
    }

    /// <summary>Returns an account's raw event-log lines (an admin backup/export).</summary>
    public IEnumerable<string> ExportAccount(Account account) =>
        persistenceService.ReadRawLog(account);

    /// <summary>
    /// Replaces <paramref name="target"/>'s entire history from an exported event log as part of
    /// an admin restore. It validates and swaps the log (backing up the old one), then re-wires the
    /// reloaded account to persistence and swaps it into the managed list. Returns the reloaded account.
    /// Throws without changing anything if the log is invalid.
    /// </summary>
    public Account ImportAccount(Account target, IEnumerable<string> lines)
    {
        var reloaded = persistenceService.ReplaceLog(target, lines, ignoreMonthClosed);

        var index = accounts.IndexOf(target);
        if (index >= 0)
        {
            target.NewEvent -= OnNewEvent;
            reloaded.NewEvent += OnNewEvent;
            accounts[index] = reloaded;
        }

        return reloaded;
    }

    private void OnNewEvent(object? sender, AccountEvent e)
    {
        if (sender is Account account)
        {
            persistenceService.Save(account, e);
        }
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
        accounts.AddRange(persistenceService.LoadAccounts(ignoreMonthClosed));
        foreach (var account in accounts)
        {
            account.NewEvent += OnNewEvent;
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