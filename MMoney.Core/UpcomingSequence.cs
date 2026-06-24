namespace MMoney.Core;

/// <summary>
/// An active <see cref="MMoney.Core.Sequence"/> paired with its next due date — the shape the repeating-items
/// list consumes. Produced by <see cref="Account.GetUpcomingSequences"/>.
/// </summary>
/// <param name="Sequence">The repeating rule.</param>
/// <param name="NextDue">The first occurrence on or after the queried date.</param>
public sealed record UpcomingSequence(Sequence Sequence, DateOnly NextDue);
