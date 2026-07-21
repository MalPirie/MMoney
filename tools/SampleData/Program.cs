// Generates a realistic sample MMoney account and writes its raw event log (nljson) to the given path.
//
//   dotnet run --project tools/SampleData -- <output-file.jsonl>
//
// The output file's name stem must be the account id (32 hex), because the app's admin Import reads the id from
// the file name (ADR-0008). The log is built through the real MMoney.Core domain — AccountPersistenceService
// encodes each event as the app itself would — so it is guaranteed valid and replayable.
//
// Shape (relative to today's month): two months before, the current month, and three after (six months). Two
// no-end monthly sequences (Salary income, Rent expense), a Weekly sequence that starts in the first month and
// ends in the second, and a no-end Yearly sequence. Each month carries roughly twenty transactions; income is
// Salary + monthly Dividends, plus at least one other random incoming.

using System.IO.Abstractions;
using MMoney.Core;
using MMoney.Core.Repeat;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: SampleData <output-file.jsonl>   (file name stem must be the account id, 32 hex)");
    return 1;
}

var outputPath = Path.GetFullPath(args[0]);
if (!Guid.TryParse(Path.GetFileNameWithoutExtension(outputPath), out var id))
{
    Console.Error.WriteLine($"the output file name stem must be an account id (GUID): '{Path.GetFileNameWithoutExtension(outputPath)}'");
    return 1;
}

var today = DateOnly.FromDateTime(DateTime.Today);
var current = new MonthOnly(today.Year, today.Month);
var first = current.Add(-2);                                              // two months before
var months = Enumerable.Range(0, 6).Select(i => first.Add(i)).ToList();   // first .. current+3 (inclusive)

var rng = new Random(0x5A3D1E);                                           // deterministic sample

// Build the log via AccountPersistenceService (writes <tempDir>/<id:N>), then copy to the requested path.
var tempDir = Path.Combine(Path.GetTempPath(), "mmoney-sample-" + id.ToString("N"));
Directory.CreateDirectory(tempDir);
var svc = new AccountPersistenceService(tempDir, new FileSystem());
var account = new Account(id, []);
account.NewEvent += (_, e) => svc.Save(account, e);                       // persist each event as it is applied

account.SetName("Sample account");

// ---- repeating sequences --------------------------------------------------------------------------------
var monthly = new RepeatStrategy.Monthly(1, DayInMonth.DayOfMonth);
var forever = new RepeatEndCondition.Forever();

// Two no-end monthly sequences: one income (Salary), one expense (Rent).
account.AddTransaction(Day(first, 25), 3200m, "Salary", monthly, forever);
account.AddTransaction(Day(first, 1), -1150m, "Rent", monthly, forever);

// A weekly sequence starting in the first month and ending in the second.
account.AddTransaction(FirstWeekday(first, DayOfWeek.Monday), -68m, "Weekly groceries",
    new RepeatStrategy.Weekly(1, DaysOfWeek.Monday),
    new RepeatEndCondition.UntilDate(current.Add(-1).LastDay));

// A no-end yearly sequence (one occurrence falls inside the six-month window).
account.AddTransaction(Day(first, 12), -540m, "Car insurance", new RepeatStrategy.Yearly(1), forever);

// ---- one-off transactions (about twenty per month, counting the occurrences above) ----------------------
string[] expenses =
[
    "Coffee", "Fuel", "Dining out", "Pharmacy", "Streaming", "Phone bill", "Electricity", "Water", "Gym",
    "Clothes", "Books", "Taxi", "Parking", "Hardware store", "Snacks", "Cinema", "Groceries top-up", "Haircut",
    "Charity", "Household",
];
string[] incomeMisc = ["Refund", "Cashback", "Interest", "Reimbursement", "Gift received"];

foreach (var m in months)
{
    var days = DateTime.DaysInMonth(m.Year, m.Month);

    // Dividends — a monthly incoming one-off.
    account.AddTransaction(Day(m, 15), Money(rng, 80, 260), "Dividend");

    // Filler expenses. Combined with the recurring occurrences overlaid into each month, this lands each month at
    // roughly twenty transactions.
    var fillers = 15 + rng.Next(0, 4); // 15..18
    for (var i = 0; i < fillers; i++)
    {
        account.AddTransaction(Day(m, rng.Next(1, days + 1)), -Money(rng, 3, 90), Pick(rng, expenses));
    }

    // At least one other random incoming (guaranteed in the current month, likely elsewhere).
    if (m.Equals(current) || rng.Next(2) == 0)
    {
        account.AddTransaction(Day(m, rng.Next(1, days + 1)), Money(rng, 20, 180), Pick(rng, incomeMisc));
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.Copy(Path.Combine(tempDir, id.ToString("N")), outputPath, overwrite: true);
Directory.Delete(tempDir, recursive: true);

Console.WriteLine($"Sample account {id:N}: {months.Count} months ({first} .. {months[^1]}), " +
    $"{File.ReadLines(outputPath).Count()} events -> {outputPath}");
return 0;

// ---- helpers --------------------------------------------------------------------------------------------
static DateOnly Day(MonthOnly m, int day) =>
    new(m.Year, m.Month, Math.Min(day, DateTime.DaysInMonth(m.Year, m.Month)));

static DateOnly FirstWeekday(MonthOnly m, DayOfWeek dow)
{
    var d = new DateOnly(m.Year, m.Month, 1);
    while (d.DayOfWeek != dow)
    {
        d = d.AddDays(1);
    }

    return d;
}

static decimal Money(Random rng, int min, int max) =>
    Math.Round((decimal)(rng.NextDouble() * (max - min) + min), 2);

static string Pick(Random rng, string[] items) => items[rng.Next(items.Length)];
