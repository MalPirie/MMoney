# Overlays are a hand-rolled layer in the shell's root Grid, not MenuFlyout or a popup library

The overflow **`Menu`** (banner three-dot → Print / Export / Settings) is the first overlay in the app, and
whatever it establishes is the pattern the rest of the controls table's overlays — **AlertDialog**, the
**radio-choice** and **date-picker** dialogs, and the **Snackbar** — will follow. We build overlays as a
**conditionally-rendered layer inside the shell's root `Grid`** (spanning all rows): a full-page tap-catcher
plus the positioned surface. No native `MenuFlyout`, no popup library.

> **Correction (ADR-0005):** an earlier draft listed "the pushed **Settings** surface" among the followers of
> this overlay pattern. That was imprecise. Settings — and Add/Edit and the repeat-strategy page — are **pushed
> pages** on a `NavigationPage` stack, not overlay layers (see ADR-0005). This overlay pattern is for the
> *non-page* overlays only: dialogs and the snackbar.

## Decision

- **The overlay is a MauiReactor layer in the app's root `Grid`, owned by the app, not the library.** When the
  menu is open, `ShellPage` renders an extra full-bleed child over its existing rows. `Mobiorum.Material3`
  owns only the presentational **`Menu`** surface (M3 `SurfaceContainer` card, Level-2 `Elevation`, ~4dp
  corners, content-sized within 112–280dp, 48dp items with a leading-icon slot) and the `MenuItem` descriptor
  (a plain config object with `OnSelected`, mirroring `NavDestination`). Placement, open/closed state
  (`ShellState.MenuOpen`), and the concrete items stay in the app, per ADR-0001's "layout/placement stays in
  the app."

- **Menus get a *transparent* tap-catcher; the dim scrim is reserved for modal dialogs.** M3 menus do not dim
  their background — the layer is a fully transparent full-page catcher whose only job is to dismiss on an
  outside tap. AlertDialog and the date-picker, being modal, will reuse this same layer *with* a dimmed scrim.
  The dismiss-on-outside-tap plumbing is shared; the dimming is the per-overlay difference.

- **No generic `OverlayHost` yet.** The app hosts this first overlay in its own root `Grid` directly. If the
  later dialogs prove they need shared overlay plumbing (scrim, focus, back-handling, stacking), we extract an
  `OverlayHost` then — not speculatively.

- **Anchoring/placement is fixed, not measured.** The menu covers its trigger (M3 overflow idiom): the surface
  is aligned top-right in the layer, ~8dp off the right edge, overlaying the banner. Because the trigger is
  pinned to a fixed corner, no anchor-bounds measurement is needed; the app owns the alignment.

- **Open/close motion via `WithAnimation`, not `AnimationController`.** A menu open/close is a discrete,
  fire-once fade + scale-from-anchor. ADR-0003 reached for `AnimationController` because a *continuous drag*
  must be grab-interruptible mid-flight; that does not apply to a menu, so the simpler tool is correct here.

## Considered and rejected

- **MAUI `MenuFlyout` / native context menu** — styling is platform-dependent and cannot hit M3 spec, and it
  fights MauiReactor's declarative model. Rejected for the same reason `TabStrip` was hand-rolled rather than
  taking `CarouselView`'s chrome: this library exists for M3 fidelity.

- **`CommunityToolkit.Maui.Popup`** — a real popup window, but a new dependency with its own
  navigation/lifecycle semantics, awkward under MauiReactor, and (being a separate window) it still leaves
  anchoring to the trigger a manual job. Heavier than the problem.

## Consequences

- Overlays are **clipped to the `ContentPage` bounds** and z-ordered manually — neither matters for a menu
  anchored inside the banner, and both are acceptable for the modal dialogs to come.
- When the planned **custom top app bar** lands, the overflow trigger moves from the banner into that bar; the
  generic `Menu` is anchor-agnostic, so only the app-owned placement moves. Android hardware-back-to-dismiss is
  deferred to that top-app-bar work.

## To review (open follow-up, 2026-07-04)

The overflow menu ships but its placement/animation is **not fully right yet** — revisit:

- At full open (`Scale 1`) the menu's top-right measures exactly onto the button's top-right corner
  (`[…,1058]` right / `103` top, from the measured button bounds `[923,103][1058,238]`), but the result still
  reads as slightly off on device to the user. Needs a fresh look at rest.
- **MauiReactor ignores `AnchorX(1)`/`HorizontalOptions` on a `Component`'s root `Border`** when it's placed in
  a grid cell, so the open animation grows from the **left** edge rather than the top-right corner (settles
  aligned, but the 150ms motion is the wrong M3 direction). A fade-only open, or animating an element the anchor
  *does* honour, is the likely fix.
- Testing note: `adb input tap` does **not** fire the MauiReactor button's `OnClicked`, so the menu can't be
  opened for measurement via `adb` — pin `Scale`/`Opacity` to measure the open geometry, or verify with a real
  finger.
