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
// We replay it to each payment's final state, then aggressively recover repeat sequences. Payments are grouped by a
// *normalised* description (case-insensitive, whitespace-collapsed), so "Henry pm" and "Henry PM" are one thing. Each
// group's dominant cadence (weekly ~7d / monthly ~30d) is detected from the median gap between dates; its occurrences
// are matched to the projected schedule grid with a tolerance, so payday drift and the odd moved date still land in
// the sequence. Amount is NOT part of the grouping: a sequence carries a base amount (its latest) and every occurrence
// whose real amount or date differs is preserved as a per-occurrence override; a genuinely missing slot is tombstoned;
// duplicates and unmatched items fall out as one-offs. The new log is built through MMoney.Core, so it is guaranteed
// valid and replayable, and a final pass verifies the rebuilt (date, amount) multiset matches the legacy final state
// exactly (lossless). The output file's name stem must be the account id (32 hex), as the app's admin Import expects
// (ADR-0008).

using System.Globalization;
using System.IO.Abstractions;
using MMoney.Core;
using MMoney.Core.Repeat;

// ---- tuning knobs ---------------------------------------------------------------------------------------
const int MinOccurrences = 3;         // a group needs at least this many matched occurrences to be a sequence
const int WeeklyToleranceDays = 3;    // how far a weekly occurrence may drift off its grid date and still match
const int MonthlyToleranceDays = 10;  // how far a monthly occurrence may drift (payday can wander end-of-month..1st)
const double MinFillRatio = 0.5;      // fraction of grid slots that must be filled for a group to count as regular
const int OngoingWindowDays = 45;     // a sequence whose last occurrence is within this of the newest data is ongoing

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

// ---- recover repeat sequences --------------------------------------------------------------------------
// Group by normalised description; within each group, detect a cadence and match occurrences to its grid.
// Aliases fold descriptions that denote the same real sequence but were typed differently over its lifetime.
// Keyed by the normalised (lower-case, whitespace-collapsed) variant → the normalised canonical it merges into.
// Matching is exact on the normalised string, so only whole descriptions listed here merge — a longer description
// that merely starts with the same word (e.g. "Henry mattress") is unaffected.
var aliases = new Dictionary<string, string>
{
    ["henry"] = "henry pm",
};
string GroupKey(string description)
{
    var normalised = Normalise(description);
    return aliases.GetValueOrDefault(normalised, normalised);
}

// Descriptions that recur often but aren't a fixed sequence — frequent variable spends (a credit-card bill paid at
// whatever the statement is) or a two-way transfer account. Kept as one-offs even when the cadence looks regular.
var notSequences = new HashSet<string>
{
    "credit card",
    "dave dunn",
    "trading 212",
};

var consumed = new HashSet<Payment>(ReferenceEqualityComparer.Instance);
var plans = new List<SeqPlan>();

foreach (var group in finals.GroupBy(p => GroupKey(p.Description)))
{
    var items = group.OrderBy(p => p.Date).ToList();
    if (notSequences.Contains(group.Key) || DetectCadence(items) is not { } cadence)
    {
        continue;
    }

    var origin = items[0].Date;
    var lastDate = items[^1].Date;
    RepeatStrategy strategy = cadence == Cadence.Weekly
        ? new RepeatStrategy.Weekly(1, WeekdayMask(origin.DayOfWeek))
        : new RepeatStrategy.Monthly(1, DayInMonth.DayOfMonth);
    var tolerance = cadence == Cadence.Weekly ? WeeklyToleranceDays : MonthlyToleranceDays;

    // The projected grid across the group's own lifespan (origin..last), and each actual matched to its nearest
    // free slot within tolerance. Iteration is date-ascending, so an earlier actual claims the earlier slot.
    var slots = ScheduleDates(strategy, origin, lastDate);
    var matched = new Dictionary<int, Payment>();
    foreach (var actual in items)
    {
        var best = -1;
        var bestDistance = int.MaxValue;
        for (var s = 0; s < slots.Count; s++)
        {
            if (matched.ContainsKey(s))
            {
                continue;
            }

            var distance = Math.Abs(slots[s].DayNumber - actual.Date.DayNumber);
            if (distance <= tolerance && distance < bestDistance)
            {
                best = s;
                bestDistance = distance;
            }
        }

        if (best >= 0)
        {
            matched[best] = actual;
        }
    }

    var fill = slots.Count == 0 ? 0 : (double)matched.Count / slots.Count;
    if (matched.Count < MinOccurrences || fill < MinFillRatio)
    {
        continue;   // not regular enough — leave the whole group as one-offs
    }

    var description = group.GroupBy(p => p.Description).OrderByDescending(g => g.Count()).First().Key;
    var baseAmount = items[^1].Amount;   // the latest amount, so future projected occurrences carry the current value
    plans.Add(new SeqPlan(origin, lastDate, strategy, cadence, description, baseAmount, slots, matched));

    foreach (var actual in matched.Values)
    {
        consumed.Add(actual);
    }
}

// ---- build the new event log through MMoney.Core -------------------------------------------------------
var tempDir = Path.Combine(Path.GetTempPath(), "mmoney-convert-" + id.ToString("N"));
Directory.CreateDirectory(tempDir);
var svc = new AccountPersistenceService(tempDir, new FileSystem());
var account = new Account(id, []);
account.NewEvent += (_, e) => svc.Save(account, e);

account.SetName("Imported account");

var amountOverrides = 0;
var dateMoves = 0;
var tombstones = 0;

foreach (var plan in plans.OrderBy(p => p.Origin))
{
    var ongoing = newest.DayNumber - plan.LastDate.DayNumber <= OngoingWindowDays;
    RepeatEndCondition end = ongoing
        ? new RepeatEndCondition.Forever()
        : new RepeatEndCondition.UntilDate(plan.LastDate);

    var seqTx = account.AddTransaction(plan.Origin, plan.BaseAmount, plan.Description, plan.Strategy, end);
    var number = seqTx.Sequence;

    // Reconcile each grid slot within the sequence's lifespan against the matched actual.
    for (var s = 0; s < plan.Slots.Count; s++)
    {
        var slotDate = plan.Slots[s];
        var slotId = new TransactionId(slotDate, number);

        if (!plan.Matched.TryGetValue(s, out var actual))
        {
            // A projected occurrence with no real payment behind it — suppress it so it never shows.
            account.RemoveTransaction(new Transaction(slotId, plan.BaseAmount, plan.Description));
            tombstones++;
            continue;
        }

        if (actual.Date != slotDate)
        {
            try
            {
                account.ChangeTransactionDate(new Transaction(slotId, plan.BaseAmount, plan.Description), actual.Date);
                dateMoves++;
            }
            catch (InvalidOperationException)
            {
                // The real date already hosts another occurrence of this sequence — drop the phantom slot and
                // keep the payment as a one-off instead, so nothing is lost or doubled.
                account.RemoveTransaction(new Transaction(slotId, plan.BaseAmount, plan.Description));
                tombstones++;
                consumed.Remove(actual);
                continue;
            }
        }

        if (actual.Amount != plan.BaseAmount)
        {
            var currentId = new TransactionId(actual.Date, number);
            account.ChangeTransactionAmount(new Transaction(currentId, plan.BaseAmount, plan.Description), actual.Amount);
            amountOverrides++;
        }
    }

    // For an ongoing (Forever) sequence, suppress phantom occurrences projected between its last real payment and
    // the newest data in the file; occurrences after the newest date are left to project as genuine upcoming items.
    if (ongoing)
    {
        foreach (var tail in ScheduleDates(plan.Strategy, plan.Origin, newest).Where(d => d > plan.LastDate))
        {
            account.RemoveTransaction(new Transaction(new TransactionId(tail, number), plan.BaseAmount, plan.Description));
            tombstones++;
        }
    }
}

// Everything not folded into a sequence — duplicates, one-off spends, collision fall-backs — stays a one-off.
foreach (var p in finals.Where(p => !consumed.Contains(p)))
{
    account.AddTransaction(p.Date, p.Amount, p.Description);
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.Copy(Path.Combine(tempDir, id.ToString("N")), outputPath, overwrite: true);
Directory.Delete(tempDir, recursive: true);

// ---- verify the rebuild is lossless --------------------------------------------------------------------
// The multiset of (date, amount) the app would display over the data's span must equal the legacy final state.
var target = Multiset(finals.Select(p => (p.Date, p.Amount)));
var rebuilt = new Dictionary<(DateOnly, decimal), int>();
if (account.EarliestContentMonth() is { } earliestMonth)
{
    foreach (var month in earliestMonth.To(MonthOnly.FromDate(newest)))
    {
        foreach (var entry in account.GetMonth(month))
        {
            if (entry.Kind != LedgerEntryKind.CarriedBalance && entry.Date <= newest)
            {
                var key = (entry.Date, entry.Amount);
                rebuilt[key] = rebuilt.GetValueOrDefault(key) + 1;
            }
        }
    }
}

var mismatches = new List<string>();
foreach (var (key, count) in target)
{
    var got = rebuilt.GetValueOrDefault(key);
    if (got != count)
    {
        mismatches.Add($"  legacy has {count}x, rebuilt has {got}x: {key.Item1:yyyy-MM-dd} {key.Item2.ToString("0.##", inv)}");
    }
}
foreach (var (key, count) in rebuilt)
{
    if (!target.ContainsKey(key))
    {
        mismatches.Add($"  rebuilt has {count}x with no legacy match: {key.Item1:yyyy-MM-dd} {key.Item2.ToString("0.##", inv)}");
    }
}

// ---- report --------------------------------------------------------------------------------------------
var events = File.ReadLines(outputPath).Count();
var oneOffs = finals.Count(p => !consumed.Contains(p));
Console.WriteLine($"Converted {finals.Count} payments -> account {id:N}: "
    + $"{plans.Count} sequence(s), {oneOffs} one-off(s), {events} events.");
Console.WriteLine($"  overrides: {amountOverrides} amount, {dateMoves} date, {tombstones} suppressed occurrence(s).");
if (skippedZero > 0 || unrecognised > 0)
{
    Console.WriteLine($"  (skipped {skippedZero} zero-amount, {unrecognised} unrecognised line(s))");
}

foreach (var plan in plans.OrderByDescending(p => p.Matched.Count))
{
    var amounts = plan.Matched.Values.Select(a => a.Amount).ToList();
    var range = amounts.Min() == amounts.Max()
        ? amounts[0].ToString("0.##", inv)
        : $"{amounts.Min().ToString("0.##", inv)}..{amounts.Max().ToString("0.##", inv)}";
    Console.WriteLine($"  {plan.Cadence.ToString().ToLowerInvariant(),-7} \"{plan.Description}\" x{plan.Matched.Count} "
        + $"[{range}] {plan.Origin:yyyy-MM-dd} .. {plan.LastDate:yyyy-MM-dd}");
}

Console.WriteLine();
if (mismatches.Count == 0)
{
    Console.WriteLine("Lossless check: PASS — every legacy transaction is reproduced exactly.");
}
else
{
    Console.WriteLine($"Lossless check: FAIL — {mismatches.Count} discrepancy(ies):");
    foreach (var m in mismatches.Take(40))
    {
        Console.WriteLine(m);
    }
}

Console.WriteLine($"-> {outputPath}");
return mismatches.Count == 0 ? 0 : 2;

// ---- helpers --------------------------------------------------------------------------------------------
decimal Amount(string s) => decimal.Parse(s.Trim(), NumberStyles.Number, inv);

static bool TryDate(string s, out DateOnly date) =>
    DateOnly.TryParseExact(s.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

// Case-insensitive, whitespace-collapsed key so casing and spacing differences group together.
static string Normalise(string description) =>
    string.Join(' ', description.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

// The dominant cadence of a group, from the median gap between its distinct dates, or null if it isn't regular.
static Cadence? DetectCadence(List<Payment> items)
{
    var dates = items.Select(p => p.Date).Distinct().OrderBy(d => d).ToList();
    if (dates.Count < 3)
    {
        return null;
    }

    var gaps = new List<int>();
    for (var i = 1; i < dates.Count; i++)
    {
        gaps.Add(dates[i].DayNumber - dates[i - 1].DayNumber);
    }

    gaps.Sort();
    var median = gaps[gaps.Count / 2];
    return median switch
    {
        >= 5 and <= 10 => Cadence.Weekly,
        >= 24 and <= 40 => Cadence.Monthly,
        _ => null,
    };
}

// System.DayOfWeek is Sunday=0..Saturday=6; DaysOfWeek flags are Monday=bit0..Sunday=bit6.
static DaysOfWeek WeekdayMask(DayOfWeek dayOfWeek) => (DaysOfWeek)(1 << (((int)dayOfWeek + 6) % 7));

// The schedule's projected occurrence dates from origin through upTo (inclusive), using the production date maths.
static List<DateOnly> ScheduleDates(RepeatStrategy strategy, DateOnly origin, DateOnly upTo)
{
    var schedule = new Schedule(strategy, new RepeatEndCondition.Forever(), origin);
    var dates = new List<DateOnly>();
    for (var month = MonthOnly.FromDate(origin); month.CompareTo(MonthOnly.FromDate(upTo)) <= 0; month = month.Add(1))
    {
        foreach (var date in schedule.DatesForMonth(month))
        {
            if (date >= origin && date <= upTo)
            {
                dates.Add(date);
            }
        }
    }

    return dates;
}

static Dictionary<(DateOnly, decimal), int> Multiset(IEnumerable<(DateOnly, decimal)> items)
{
    var counts = new Dictionary<(DateOnly, decimal), int>();
    foreach (var item in items)
    {
        counts[item] = counts.GetValueOrDefault(item) + 1;
    }

    return counts;
}

enum Cadence
{
    Weekly,
    Monthly,
}

sealed class Payment
{
    public string Description = "";
    public decimal Amount;
    public DateOnly Date;
}

sealed record SeqPlan(
    DateOnly Origin,
    DateOnly LastDate,
    RepeatStrategy Strategy,
    Cadence Cadence,
    string Description,
    decimal BaseAmount,
    List<DateOnly> Slots,
    Dictionary<int, Payment> Matched);
