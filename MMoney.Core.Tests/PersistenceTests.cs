using System.IO.Abstractions.TestingHelpers;
using MMoney.Core;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class PersistenceTests
{
    private const string Path = @"C:\data";

    private static AccountPersistenceService NewService(out MockFileSystem fileSystem)
    {
        fileSystem = new MockFileSystem();
        fileSystem.Directory.CreateDirectory(Path);
        return new AccountPersistenceService(Path, fileSystem);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEventsAndState()
    {
        var service = NewService(out _);
        var id = Guid.NewGuid();
        var account = new Account(id, []);
        account.NewEvent += (_, e) => service.Save(account, e);

        account.SetName("Current");
        account.AddTransaction(D(2026, 1, 5), 50m, "deposit");

        var loaded = service.LoadAccounts(ignoreMonthClosed: false).ToList();

        Assert.Single(loaded);
        Assert.Equal(id, loaded[0].Id);
        Assert.Equal("Current", loaded[0].Name);
        Assert.Equal(50m, loaded[0].BalanceOn(D(2026, 1, 31)));
    }

    [Fact]
    public void DeletedAccounts_AreExcludedFromLoadButListedAsDeleted()
    {
        var service = NewService(out _);
        var account = new Account(Guid.NewGuid(), []);
        account.NewEvent += (_, e) => service.Save(account, e);
        account.SetName("Doomed");
        account.AddTransaction(D(2026, 1, 5), 10m, "x");

        service.MarkAccountAsDeleted(account);

        Assert.Empty(service.LoadAccounts(false));
        var deleted = service.GetDeletedAccounts().ToList();
        Assert.Single(deleted);
        Assert.Equal(account.Id, deleted[0].Id);
        Assert.Equal("Doomed", deleted[0].Name);
    }

    [Fact]
    public void RestoreAccount_MakesItLoadableAgain()
    {
        var service = NewService(out _);
        var account = new Account(Guid.NewGuid(), []);
        account.NewEvent += (_, e) => service.Save(account, e);
        account.SetName("Back");
        service.MarkAccountAsDeleted(account);

        var restored = service.RestoreAccount(account.Id, ignoreMonthClosed: false);

        Assert.Equal("Back", restored.Name);
        Assert.Single(service.LoadAccounts(false));
        Assert.Empty(service.GetDeletedAccounts());
    }

    [Fact]
    public void ReplaceLog_WithInvalidLines_ThrowsAndLeavesTheOriginalUntouched()
    {
        var service = NewService(out _);
        var account = new Account(Guid.NewGuid(), []);
        account.NewEvent += (_, e) => service.Save(account, e);
        account.SetName("Safe");
        account.AddTransaction(D(2026, 1, 5), 10m, "x");
        var original = service.ReadRawLog(account).ToList();

        Assert.ThrowsAny<Exception>(() => service.ReplaceLog(account, ["this is not json"], ignoreMonthClosed: false));

        Assert.Equal(original, service.ReadRawLog(account).ToList());
    }

    [Fact]
    public void ReplaceLog_WithValidLines_SwapsTheLogAndReloads()
    {
        var service = NewService(out _);
        var source = new Account(Guid.NewGuid(), []);
        source.NewEvent += (_, e) => service.Save(source, e);
        source.SetName("Source");
        source.AddTransaction(D(2026, 1, 5), 80m, "x");
        var exported = service.ReadRawLog(source).ToList();

        var target = new Account(Guid.NewGuid(), []);
        target.NewEvent += (_, e) => service.Save(target, e);
        target.SetName("Target");

        var reloaded = service.ReplaceLog(target, exported, ignoreMonthClosed: false);

        Assert.Equal(target.Id, reloaded.Id);          // same identity
        Assert.Equal("Source", reloaded.Name);          // source's history
        Assert.Equal(80m, reloaded.BalanceOn(D(2026, 1, 31)));
    }
}
