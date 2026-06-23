using System.Text.Json.Serialization;
using MMoney.Core.Repeat;

namespace MMoney.Core;

/// <summary>
/// Discriminated union of all events that can be applied to an <see cref="Account"/>.
/// Events are persisted as newline-delimited JSON using the <c>$type</c> discriminator.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(NameSet), "nameSet")]
[JsonDerivedType(typeof(TransactionAdded), "transactionAdded")]
[JsonDerivedType(typeof(TransactionAmountChanged), "transactionAmountChanged")]
[JsonDerivedType(typeof(TransactionDateChanged), "transactionDateChanged")]
[JsonDerivedType(typeof(TransactionDescriptionChanged), "transactionDescriptionChanged")]
[JsonDerivedType(typeof(TransactionRemoved), "transactionRemoved")]
[JsonDerivedType(typeof(SequenceRemoved), "sequenceRemoved")]
[JsonDerivedType(typeof(SequenceAmountChanged), "sequenceAmountChanged")]
[JsonDerivedType(typeof(SequenceDescriptionChanged), "sequenceDescriptionChanged")]
[JsonDerivedType(typeof(MonthClosed), "monthClosed")]
public abstract record AccountEvent
{
    private AccountEvent() { }

    /// <summary>The account was given a new name.</summary>
    public sealed record NameSet(string Name) : AccountEvent;

    /// <summary>A new transaction was added to the account.</summary>
    public sealed record TransactionAdded(
        DateOnly Date,
        int Sequence,
        decimal Amount,
        string Description,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RepeatStrategy? Strategy = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RepeatEndCondition? EndCondition = null) : AccountEvent;

    /// <summary>The amount of an existing transaction was changed.</summary>
    public sealed record TransactionAmountChanged(DateOnly Date, int Sequence, decimal NewAmount) : AccountEvent;

    /// <summary>The date of an existing transaction was changed.</summary>
    public sealed record TransactionDateChanged(DateOnly Date, int Sequence, DateOnly NewDate) : AccountEvent;

    /// <summary>The description of an existing transaction was changed.</summary>
    public sealed record TransactionDescriptionChanged(DateOnly Date, int Sequence, string NewDescription) : AccountEvent;

    /// <summary>An existing transaction was removed from the account.</summary>
    public sealed record TransactionRemoved(DateOnly Date, int Sequence) : AccountEvent;

    /// <summary>The given month was closed: all its transactions removed and a "Balance carried"
    /// opening entry added as the first transaction of the following month.</summary>
    public sealed record MonthClosed(MonthOnly Month) : AccountEvent;

    /// <summary>A repeating sequence was truncated at <see cref="FromDate"/> (occurrences from that date
    /// onwards removed); when <see cref="FromDate"/> equals the origin date, the whole sequence is removed.</summary>
    public sealed record SequenceRemoved(DateOnly Date, int Sequence, DateOnly FromDate) : AccountEvent;

    /// <summary>The amount of an entire repeating sequence was changed, in place, across template and all occurrences.</summary>
    public sealed record SequenceAmountChanged(DateOnly Date, int Sequence, decimal NewAmount) : AccountEvent;

    /// <summary>The description of an entire repeating sequence was changed, in place, across template and all occurrences.</summary>
    public sealed record SequenceDescriptionChanged(DateOnly Date, int Sequence, string NewDescription) : AccountEvent;
}
