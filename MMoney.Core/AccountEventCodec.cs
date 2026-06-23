using System.Text.Json;

namespace MMoney.Core;

/// <summary>
/// Owns the on-disk event-log format of one JSON-serialized <see cref="AccountEvent"/> per line
/// (newline-delimited JSON), using the <c>$type</c> discriminator declared on <see cref="AccountEvent"/>.
/// </summary>
internal static class AccountEventCodec
{
    /// <summary>
    /// Lazily decodes event-log lines into events. Throws if any line is malformed, carries an
    /// unrecognised <c>$type</c> or decodes to <see langword="null"/>. A corrupt or invalid log 
    /// fails the load loudly rather than silently dropping events.
    /// </summary>
    public static IEnumerable<AccountEvent> Decode(IEnumerable<string> lines) =>
        lines.Select(line => JsonSerializer.Deserialize<AccountEvent>(line)
            ?? throw new JsonException("Event log line decoded to null."));

    /// <summary>Encodes events as event-log lines, lazily.</summary>
    public static IEnumerable<string> Encode(IEnumerable<AccountEvent> events) =>
        events.Select(e => JsonSerializer.Serialize(e));
}
