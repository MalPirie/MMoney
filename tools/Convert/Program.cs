// Converts a legacy account file into the new nljson event log.
//
//   dotnet run --project tools/Convert -- <input.txt> <output.jsonl>
//
// The legacy format is a comma-separated event log keyed by a per-payment GUID:
//   AddPayment,<id>,<description>,<amount>,<yyyyMMdd>
//   UpdatePaymentAmount,<id>,<amount>
//   UpdatePaymentDate,<id>,<yyyyMMdd>
//   UpdatePaymentDescription,<id>,<description>
//   RemovePayment,<id>
//
// We replay it to each payment's final state, then *attempt* to recover repeat sequences: a run of payments with an
// identical description + amount, on the same day of the month, in consecutive months (>= 3), becomes a Monthly
// sequence — ongoing (Forever) if it reaches the newest date in the file, otherwise ending at its last occurrence.
// Everything else is emitted as a one-off. The new log is built through MMoney.Core, so it is guaranteed valid and
// replayable; the output file's name stem must be the account id (32 hex), as the app's admin Import expects (ADR-0008).

using System.Globalization;
using System.IO.Abstractions;
using MMoney.Core;
using MMoney.Core.Repeat;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: Convert <input.txt> <output.jsonl>   (output stem must be the account id, 32 hex)");
    return 1;
}

var inputPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
if (!Guid.TryParse(Path.GetFileNameWithoutExtension(outputPath), out var id))
{
    Console.Error.WriteLine($"the output file name stem must be an account id (GUID): '{Path.GetFileNameWithoutExtension(outputPath)}'");
    return 1;
}
if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"input not found: {inputPath}");
    return 1;
}

var inv = CultureInfo.InvariantCulture;

// ---- replay the legacy log into each payment's final state ---------------------------------------------
var payments = new Dictionary<string, Payment>();
var order = new List<string>();   // preserves add order, for a stable one-off ordering
var lineNo = 0;
var unrecognised = 0;

foreach (var raw in File.ReadLines(inputPath))
{
    lineNo++;
    var line = raw.Trim();
    if (line.Length == 0)
    {
        continue;
    }

    var f = line.Split(',');
    switch (f[0])
    {
        case "AddPayment" when f.Length == 5 && TryDate(f[4], out var date):
            if (!payments.ContainsKey(f[1]))
            {
                order.Add(f[1]);
            }

            payments[f[1]] = new Payment { Description = f[2].Trim(), Amount = Amount(f[3]), Date = date };
            break;

        case "UpdatePaymentAmount" when f.Length == 3 && payments.TryGetValue(f[1], out var pa):
            pa.Amount = Amount(f[2]);
            break;

        case "UpdatePaymentDate" when f.Length == 3 && payments.TryGetValue(f[1], out var pd) && TryDate(f[2], out var nd):
            pd.Date = nd;
            break;

        case "UpdatePaymentDescription" when f.Length == 3 && payments.TryGetValue(f[1], out var pn):
            pn.Description = f[2].Trim();
            break;

        case "RemovePayment" when f.Length == 2:
            payments.Remove(f[1]);
            break;

        default:
            Console.Error.WriteLine($"line {lineNo}: unrecognised, skipped: {line}");
            unrecognised++;
            break;
    }
}

// Final payments in add order, dropping any the new model can't hold (zero amount) and fixing blank descriptions.
var skippedZero = 0;
var finals = order.Where(payments.ContainsKey).Select(g => payments[g])
    .Where(p =>
    {
        if (p.Amount != 0m)
        {
            return true;
        }

        skippedZero++;
        return false;
    })
    .Select(p => new Payment
    {
        Description = string.IsNullOrWhiteSpace(p.Description) ? "(no description)" : p.Description,
        Amount = p.Amount,
        Date = p.Date,
    })
    .ToList();

var newest = finals.Count > 0 ? finals.Max(p => p.Date) : DateOnly.MinValue;

// ---- attempt to recover monthly repeat sequences -------------------------------------------------------
// Within each (description, amount) group, find maximal runs on the same day of month in consecutive months.
var consumed = new HashSet<Payment>(ReferenceEqualityComparer.Instance);
var sequences = new List<Payment[]>();

foreach (var group in finals.GroupBy(p => (p.Description, p.Amount)))
{
    var items = group.OrderBy(p => p.Date).ToList();
    var i = 0;
    while (i < items.Count)
    {
        var day = items[i].Date.Day;
        var j = i;
        // Extend the run while each next payment is exactly one month on (day <= 28 keeps the day stable across
        // every month, so the projected schedule reproduces the run without month-length clamping).
        if (day <= 28)
        {
            while (j + 1 < items.Count && items[j + 1].Date == items[j].Date.AddMonths(1))
            {
                j++;
            }
        }

        var run = items.Skip(i).Take(j - i + 1).ToArray();
        if (day <= 28 && run.Length >= 3)
        {
            sequences.Add(run);
            foreach (var p in run)
            {
                consumed.Add(p);
            }
        }

        i = j + 1;
    }
}

// ---- build the new event log through MMoney.Core -------------------------------------------------------
var tempDir = Path.Combine(Path.GetTempPath(), "mmoney-convert-" + id.ToString("N"));
Directory.CreateDirectory(tempDir);
var svc = new AccountPersistenceService(tempDir, new FileSystem());
var account = new Account(id, []);
account.NewEvent += (_, e) => svc.Save(account, e);

account.SetName("Imported account");

foreach (var run in sequences)
{
    var origin = run[0];
    // Ongoing if the run reaches the newest data (still active), else it ends at its last occurrence.
    RepeatEndCondition end = newest.DayNumber - run[^1].Date.DayNumber <= 40
        ? new RepeatEndCondition.Forever()
        : new RepeatEndCondition.UntilDate(run[^1].Date);
    account.AddTransaction(origin.Date, origin.Amount, origin.Description,
        new RepeatStrategy.Monthly(1, DayInMonth.DayOfMonth), end);
}

foreach (var p in finals.Where(p => !consumed.Contains(p)))
{
    account.AddTransaction(p.Date, p.Amount, p.Description);
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.Copy(Path.Combine(tempDir, id.ToString("N")), outputPath, overwrite: true);
Directory.Delete(tempDir, recursive: true);

var events = File.ReadLines(outputPath).Count();
Console.WriteLine($"Converted {finals.Count} payments -> account {id:N}: "
    + $"{sequences.Count} monthly sequence(s), {finals.Count - consumed.Count} one-off(s), {events} events.");
if (skippedZero > 0 || unrecognised > 0)
{
    Console.WriteLine($"  (skipped {skippedZero} zero-amount, {unrecognised} unrecognised line(s))");
}
foreach (var run in sequences.OrderByDescending(r => r.Length))
{
    Console.WriteLine($"  sequence: \"{run[0].Description}\" {run[0].Amount.ToString("0.##", inv)} "
        + $"x{run.Length} monthly, day {run[0].Date.Day}, {run[0].Date:yyyy-MM} .. {run[^1].Date:yyyy-MM}");
}
Console.WriteLine($"-> {outputPath}");
return 0;

// ---- helpers --------------------------------------------------------------------------------------------
decimal Amount(string s) => decimal.Parse(s.Trim(), NumberStyles.Number, inv);

static bool TryDate(string s, out DateOnly date) =>
    DateOnly.TryParseExact(s.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

sealed class Payment
{
    public string Description = "";
    public decimal Amount;
    public DateOnly Date;
}
