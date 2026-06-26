# Dev scripts (StripPager investigation)

PowerShell scripts for the Android build/deploy/log loop. Run from the repo root.
All auto-discover `adb` (PATH, then `%LOCALAPPDATA%\Android\Sdk`, then `C:\Program Files (x86)\Android\android-sdk`)
and the attached device (pass `-Serial <serial>` if more than one).

| Script | What it does |
| --- | --- |
| `.\scripts\build.ps1` | Build the app for `net10.0-android` (no device). `-Quiet` for terse output. |
| `.\scripts\clear-logs.ps1` | Clear logcat and bump the buffer to 16M (so bursts aren't dropped). |
| `.\scripts\deploy.ps1` | Clear logcat, then build + install + launch on the device. `-Clean` wipes obj/bin + uninstalls first. |
| `.\scripts\logs.ps1` | Dump the buffer to `scripts/logs/` and print the `[StripPager]` trace. `-All` also shows Choreographer jank; `-Follow` streams live; `-Out <path>` to choose the file. |

## Typical loop

```powershell
.\scripts\deploy.ps1        # build, install, launch (logcat cleared)
# ... reproduce a gesture on the device ...
.\scripts\logs.ps1          # see the [StripPager] trace
```

## The trace

`StripPager.cs` logs (search the source for **AREA OF INTEREST**):

- `RENDER #n frac= scroll=` — every render; the `#n` **resetting to #1** means the control was rebuilt.
- `OnMounted` / `OnWillUnmount` — control lifecycle; watch these around a commit (recreation-on-commit).
- `PAN <status> axis= vel= frac=` — every non-Running pan status. **The key question:** does `Completed`/`Canceled` fire on release? (Under the reverted native drive it did not, on fast flicks.)
- `RELEASE vel= flick= frac= decision=` — the settle decision taken on release.
- `TAP offset= scroll=` — a strip-cell tap.

Logging is deliberately light (no per-frame spam) so logcat doesn't drop the transitions.

## Gotcha: launch crash `No view found for id 0x… (jumpToStart)`

If the app crashes on launch with a fragment error like
`No view found for id 0x7f0800f8 … NavigationRootManager_ElementBasedFragment`, that is **not** a code bug —
it's a stale Android resource table from incremental builds (the compiled id no longer matches the resource
table). Fix: `\.\scripts\deploy.ps1 -Clean` (wipes obj/bin, uninstalls, rebuilds from scratch).
