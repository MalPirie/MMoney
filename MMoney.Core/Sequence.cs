using MMoney.Core.Repeat;

namespace MMoney.Core;

/// <summary>
/// A repeating transaction rule: an origin, an amount and description, and the schedule by which it recurs.
/// The single home of <see cref="RepeatStrategy"/> and <see cref="RepeatEndCondition"/> — individual
/// occurrences are projected from a sequence and do not carry the rule themselves.
/// </summary>
/// <remarks>
/// Identified by <see cref="Id"/>: its origin date and the per-account sequence number it shares with every
/// occurrence it projects.
/// </remarks>
public sealed record Sequence(
    TransactionId Id,
    decimal Amount,
    string Description,
    RepeatStrategy Strategy,
    RepeatEndCondition EndCondition)
{
    /// <summary>The date the sequence first occurs.</summary>
    public DateOnly Origin => Id.Date;

    /// <summary>The per-account sequence number shared by the sequence and all of its occurrences.</summary>
    public int Number => Id.Sequence;
}
