using System.Text.Json.Serialization;

namespace MMoney.Core.Repeat;

/// <summary>
/// When a <see cref="RepeatStrategy"/> stops producing occurrences. Serialized alongside the strategy using
/// the <c>$until</c> discriminator.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$until")]
[JsonDerivedType(typeof(Forever), "forever")]
[JsonDerivedType(typeof(AfterOccurrences), "occurrences")]
[JsonDerivedType(typeof(UntilDate), "date")]
public abstract record RepeatEndCondition
{
    private RepeatEndCondition() { }

    /// <summary>Never stops.</summary>
    public sealed record Forever() : RepeatEndCondition;

    /// <summary>Stops after <paramref name="Occurrences"/> occurrences, counting from the origin (inclusive).</summary>
    /// <param name="Occurrences">The total number of occurrences produced.</param>
    public sealed record AfterOccurrences(int Occurrences) : RepeatEndCondition;

    /// <summary>Stops after <paramref name="Date"/> (inclusive); no occurrence falls later.</summary>
    /// <param name="Date">The last date on which an occurrence may fall.</param>
    public sealed record UntilDate(DateOnly Date) : RepeatEndCondition;
}
