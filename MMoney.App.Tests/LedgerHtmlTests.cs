using MMoney.App.Export;
using MMoney.Core;
using Xunit;

namespace MMoney.App.Tests;

public class LedgerHtmlTests
{
    [Fact]
    public void Build_IncludesTitleAsOfDateAndHeaders()
    {
        var html = LedgerHtml.Build([], "My Account", new DateOnly(2026, 7, 20));

        Assert.Contains("<h1>My Account</h1>", html);
        Assert.Contains("Statement as of 20 July 2026", html);
        Assert.Contains("<th>Date</th>", html);
        Assert.Contains("<th class=\"num\">Balance</th>", html);
    }

    [Fact]
    public void Build_BlankTitle_FallsBackToAccount()
    {
        var html = LedgerHtml.Build([], "   ", new DateOnly(2026, 7, 20));

        Assert.Contains("<h1>Account</h1>", html);
    }

    [Fact]
    public void Build_RendersRowWithGbCurrencyAndLongDate()
    {
        var rows = new[] { Row(new DateOnly(2026, 7, 17), -99m, -81.11m, "Subscpto") };

        var html = LedgerHtml.Build(rows, "Acc", new DateOnly(2026, 7, 20));

        Assert.Contains("<td>17 Jul 2026</td>", html);
        Assert.Contains("<td>Subscpto</td>", html);
        Assert.Contains("£99.00", html);   // -£99.00 amount
        Assert.Contains("£81.11", html);    // -£81.11 balance
    }

    [Fact]
    public void Build_UsesNoColour_BlackAndWhiteOnly()
    {
        var rows = new[]
        {
            Row(new DateOnly(2026, 7, 1), 100m, 100m, "In"),
            Row(new DateOnly(2026, 7, 2), -30m, 70m, "Out"),
        };

        var html = LedgerHtml.Build(rows, "Acc", new DateOnly(2026, 7, 20));

        Assert.DoesNotContain("neg", html);  // no negative-amount colour class
        Assert.DoesNotContain("#b00", html); // no red — black and white only
    }

    [Fact]
    public void Build_EscapesHtmlInDescription()
    {
        var rows = new[] { Row(new DateOnly(2026, 7, 1), 1m, 1m, "A & B <tag> \"q\"") };

        var html = LedgerHtml.Build(rows, "Acc", new DateOnly(2026, 7, 20));

        Assert.Contains("A &amp; B &lt;tag&gt; &quot;q&quot;", html);
        Assert.DoesNotContain("<tag>", html);
    }

    private static LedgerEntry Row(DateOnly date, decimal amount, decimal balance, string description) =>
        new(new Transaction(date, 1, amount, description), balance, LedgerEntryKind.OneOff);
}
