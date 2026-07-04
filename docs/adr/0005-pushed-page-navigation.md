# Pushed pages use MauiReactor navigation, not an in-shell overlay layer

The Settings page (app-design §9) is the app's **first pushed page**, so it establishes the mechanism the
other two pushed pages — Add/Edit (§7) and the repeat-strategy page (§8) — will reuse. Until now the shell was
a single `ContentPage` rooted directly at `ShellPage`, with an in-shell tab swap for the two destinations and
one **overlay layer** in the root `Grid` for the overflow menu (ADR-0004). Settings forced the choice: another
overlay, or a real navigation stack.

We wrap the app root in a **`NavigationPage`** and push pages onto it with `MauiReactor.Navigation.PushAsync`.

## Decision

- **A thin `AppRoot` component hosts a `NavigationPage(new ShellPage())`.** `MauiProgram` now roots the app at
  `AppRoot` instead of `ShellPage`. `ShellPage` is otherwise unchanged; it simply becomes the navigation
  stack's root page.

- **Settings is a pushed `ContentPage`, not a state-toggled layer.** The overflow-menu "Settings" item calls
  `MauiReactor.Navigation.PushAsync<SettingsPage>()` (after closing the menu). Add/Edit and the repeat-strategy
  page will push the same way.

- **"Normal back page actions close it" comes for free.** The Android hardware **back** button and the OS back
  stack pop the page with a native page-slide transition — no manual back-interception (which ADR-0004 had
  explicitly deferred). A pushed page is a *page on a back stack*, which is exactly the semantic the feature
  asked for.

- **The native navigation bar stays hidden everywhere** (`HasNavigationBar(false)` on each `ContentPage`). MMoney
  draws its own **primary-coloured** chrome — the balance banner on the shell, a generic `TopAppBar` on pushed
  pages (app-design §2). The `NavigationPage` exists only to provide the push/pop stack, not its chrome.

## Considered and rejected

- **An in-shell overlay layer for Settings** (the ADR-0004 pattern: a full-bleed child of the root `Grid`,
  toggled by `ShellState`). It would have avoided the app-root restructure and reused the menu's overlay
  machinery, but: hardware-back would need **manual interception**, there is **no page transition**, and it
  contradicts app-design §3 ("MauiReactor push-navigation for pushed pages"). The overlay pattern is right for
  the *non-page* overlays it was designed for — dialogs, the snackbar — but Settings is a genuine page.

## Consequences

- The app now has a navigation stack; §7 and §8 ride it with no further plumbing.
- ADR-0004's phrasing listed "the pushed Settings surface" among the overlay-pattern followers — that was
  imprecise and is corrected there: Settings/Add-Edit/repeat-strategy are **pushed pages** (this ADR); the
  overlay layer is for AlertDialog, the radio-choice/date-picker dialogs, and the Snackbar.
- The generic `TopAppBar` (`Mobiorum.Material3`, app-design §1) lands with this work: a leading back button + a
  title, defaulting to M3 `surface` colours with a container-colour knob so the app can set `primary`/`onPrimary`
  to match the banner.
