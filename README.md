# MMoney

A personal-finance ledger built around an **event-sourced core**. Account state is rebuilt by replaying an
event log; repeating transactions and running balances are projected and computed on demand over a sparse
overlay — nothing is eagerly materialised.

The core (`MMoney.Core`) is the mature part of the project and is thoroughly tested. The app
(`MMoney.App`, .NET MAUI via [MauiReactor](https://github.com/adospace/reactorui-maui)) is currently the
starting template and not yet wired to the core.

## Highlights

- **Event-sourced.** Every change is an `AccountEvent`; replaying the log reconstructs the account. The log
  is a pure-data contract, independent of how reads are computed.
- **Projected reads.** Repeating *sequences* (daily / weekly / monthly / yearly, with end conditions) are
  projected into occurrences on read; only facts and per-occurrence edits ("overrides") are stored.
- **Computed balances.** Running balances are never stored — they're computed forward from the
  "balance carried" anchor written when a month is closed.
- **Closed months.** Old months can be collapsed (and optionally browsed read-only), with an always-enforced
  edit lock.

## Layout

| Project | What it is |
|---|---|
| `MMoney.Core` | The event-sourced ledger core (`net10.0`). No UI dependencies. |
| `MMoney.Core.Tests` | xUnit test suite (61 tests) over the core's public API and persistence layer. |
| `MMoney.App` | .NET MAUI / MauiReactor front-end (Android + Windows). Currently the stock template. |

## Documentation

- [`CONTEXT.md`](CONTEXT.md) — domain glossary (Account, Sequence, Occurrence, edit lock, projection floor).
- [`docs/core-architecture.md`](docs/core-architecture.md) — how the core works, its invariants, the
  decisions behind it, and likely future changes. **Start here.**
- [`docs/model-b-read-model.md`](docs/model-b-read-model.md) — deep dive on the sparse-overlay read model.

## Building and testing

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/). The MAUI app additionally needs the MAUI
workloads (`dotnet workload install maui`).

```sh
# Core only (no MAUI workloads needed)
dotnet build MMoney.Core/MMoney.Core.csproj
dotnet test  MMoney.Core.Tests/MMoney.Core.Tests.csproj

# Whole solution (needs MAUI workloads for the app)
dotnet build MMoney.slnx
```

## License

[MIT](LICENSE) — free to use, modify, and distribute, provided the copyright notice and license text are
retained.
