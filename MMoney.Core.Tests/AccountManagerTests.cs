using System.IO.Abstractions.TestingHelpers;
using MMoney.Core;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class AccountManagerTests
{
    private const string Path = @"C:\data";

    private static AccountManager NewManager(MockFileSystem fileSystem, bool ignoreMonthClosed = false)
    {
        if (!fileSystem.Directory.Exists(Path))
        {
            fileSystem.Directory.CreateDirectory(Path);
        }

        return new AccountManager(new AccountPersistenceService(Path, fileSystem), ignoreMonthClosed, Clock());
    }

    [Fact]
    public void EmptyStorage_CreatesADefaultAccount()
    {
        var manager = NewManager(new MockFileSystem());

        var accounts = manager.GetAccounts();
        Assert.Single(accounts);
        Assert.Equal("New Account", accounts[0].Name);
    }

    [Fact]
    public void Today_ComesFromTheInjectedClock()
    {
        var manager = NewManager(new MockFileSystem());

        Assert.Equal(D(2026, 6, 23), manager.Today);
    }

    [Fact]
    public void AddAccount_PersistsAndIsReloadedByAFreshManager()
    {
        var fileSystem = new MockFileSystem();
        var manager = NewManager(fileSystem);
        var savings = manager.AddAccount("Savings");
        savings.AddTransaction(D(2026, 1, 10), 100m, "deposit");

        var reloaded = NewManager(fileSystem).GetAccounts().Single(a => a.Name == "Savings");

        Assert.Equal(100m, reloaded.BalanceOn(D(2026, 1, 31)));
    }

    [Fact]
    public void AddAccount_RejectsDuplicateNames()
    {
        var manager = NewManager(new MockFileSystem());
        manager.AddAccount("Bills");

        Assert.Throws<ArgumentException>(() => manager.AddAccount("Bills"));
    }

    [Fact]
    public void RemoveThenRestore_RoundTripsAnAccount()
    {
        var manager = NewManager(new MockFileSystem());
        var temp = manager.AddAccount("Temp");

        Assert.True(manager.RemoveAccount(temp));
        Assert.DoesNotContain(manager.GetAccounts(), a => a.Name == "Temp");

        var deleted = manager.GetDeletedAccounts().Single(d => d.Name == "Temp");
        var restored = manager.RestoreAccount(deleted.Id);

        Assert.Equal("Temp", restored.Name);
        Assert.Contains(manager.GetAccounts(), a => a.Id == restored.Id);
    }

    [Fact]
    public void RestoreAccount_GivesAUniqueNameWhenTheOriginalIsTaken()
    {
        var manager = NewManager(new MockFileSystem());
        var first = manager.AddAccount("Holiday");
        manager.RemoveAccount(first);
        manager.AddAccount("Holiday"); // reuse the name while the original is deleted

        var deleted = manager.GetDeletedAccounts().Single();
        var restored = manager.RestoreAccount(deleted.Id);

        Assert.Equal("Holiday (2)", restored.Name);
    }

    [Fact]
    public void SetIgnoreMonthClosed_ReloadsAccountsAndRevealsClosedMonths()
    {
        var manager = NewManager(new MockFileSystem());
        var main = manager.AddAccount("Main");
        main.AddTransaction(D(2026, 1, 10), 100m, "a");
        main.AddTransaction(D(2026, 2, 10), -40m, "b");
        main.CloseMonth(M(2026, 1), today: D(2026, 2, 15));

        Assert.Empty(main.GetMonth(M(2026, 1))); // collapsed in normal mode

        manager.SetIgnoreMonthClosed(true);
        var shown = manager.GetAccounts().Single(a => a.Id == main.Id);

        Assert.NotSame(main, shown);                          // reloaded instance
        Assert.Single(shown.GetMonth(M(2026, 1)));            // closed month now visible
        Assert.Equal(D(2026, 2, 1), shown.EarliestAllowedDate); // lock preserved
    }

    [Fact]
    public void ImportAccount_ReplacesTargetHistoryFromAnExportedLog()
    {
        var manager = NewManager(new MockFileSystem());
        var source = manager.AddAccount("Source");
        source.AddTransaction(D(2026, 1, 5), 80m, "x");
        var exported = manager.ExportAccount(source).ToList();

        var target = manager.AddAccount("Target");
        var imported = manager.ImportAccount(target, exported);

        Assert.Equal(target.Id, imported.Id);
        Assert.Equal("Source", imported.Name);
        Assert.Equal(80m, imported.BalanceOn(D(2026, 1, 31)));
        Assert.Contains(manager.GetAccounts(), a => a.Id == target.Id && a.Name == "Source");
    }

    [Fact]
    public void ImportAccount_OffListTarget_AdoptsAndWiresTheReloadedAccount()
    {
        var fileSystem = new MockFileSystem();
        var manager = NewManager(fileSystem);
        var source = manager.AddAccount("Source");
        source.AddTransaction(D(2026, 1, 5), 80m, "x");
        var exported = manager.ExportAccount(source).ToList();

        // A target this manager does not hold — the previously-unwired branch.
        var foreign = new Account(Guid.NewGuid(), []);
        var imported = manager.ImportAccount(foreign, exported);

        // Adopted into management...
        Assert.Contains(manager.GetAccounts(), a => ReferenceEquals(a, imported));

        // ...and wired: a later event on it persists and survives a fresh-manager reload.
        imported.AddTransaction(D(2026, 2, 1), 20m, "y");
        var reloaded = NewManager(fileSystem).GetAccounts().Single(a => a.Id == imported.Id);
        Assert.Equal(100m, reloaded.BalanceOn(D(2026, 2, 28)));
    }
}
