using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using MauiReactor;
using MauiReactor.Shapes;
using Mobiorum.Material3;
using MMoney.Core;

namespace MMoney.App.Components;

/// <summary>Props for the pushed <see cref="SettingsPage"/>.</summary>
internal sealed class SettingsProps
{
    /// <summary>Invoked after a change that the shell must reflect (e.g. closing a month advances the edit lock).</summary>
    public Action? OnChanged { get; set; }

    /// <summary>Invoked after an admin import swaps the account wholesale, so the shell resets its shown month.</summary>
    public Action? OnAccountReplaced { get; set; }
}

internal sealed class SettingsState
{
    /// <summary>The theme option currently highlighted in the selector (mirrors <see cref="ThemePreference.Current"/>).</summary>
    public AppTheme Theme { get; set; }

    /// <summary>The event-sourced ledger, resolved from DI on mount (to apply the allow-close preference live).</summary>
    public AccountManager? Manager { get; set; }

    /// <summary>Whether closing months is allowed (mirrors <see cref="MonthClosePreference.Allowed"/>).</summary>
    public bool AllowClose { get; set; }

    /// <summary>Whether the hidden admin section is revealed (mirrors <see cref="AdminMode.Enabled"/>).</summary>
    public bool Admin { get; set; }

    /// <summary>Consecutive quick taps on the About box, and the tick of the last one — the unlock gesture.</summary>
    public int AboutTaps { get; set; }
    public long LastTapTicks { get; set; }

    // ---- import flow (admin) ----
    /// <summary>Whether the import confirm dialog is open.</summary>
    public bool ConfirmOpen { get; set; }

    /// <summary>The validated summary of the picked backup (null while the dialog is closed).</summary>
    public ImportPreview? Preview { get; set; }

    /// <summary>The id parsed from the picked file's name, and the raw lines to import.</summary>
    public Guid BackupId { get; set; }
    public List<string>? ImportLines { get; set; }

    /// <summary>Whether the backup's id differs from the current account's (a different-account/install restore).</summary>
    public bool Mismatch { get; set; }

    // ---- snackbar (host-owned auto-dismiss) ----
    public bool SnackbarOpen { get; set; }
    public string SnackbarText { get; set; } = string.Empty;
    public int SnackbarToken { get; set; }
}

/// <summary>
/// The Settings page (app-design §9): pushed over the shell (ADR-0005) with its own primary-coloured
/// <see cref="TopAppBar"/>. Content is grouped into rounded <c>surfaceContainer</c> boxes (the ledger day-box
/// idiom): an "About" box, a "Theme" box, a "Close months" switch, and — once unlocked by five quick taps on the
/// About box — an "Admin" box for raw account export / import (ADR-0008).
/// </summary>
partial class SettingsPage : Component<SettingsState, SettingsProps>
{
    private const long TapWindowMs = 2000; // resets the run if taps are further apart than this
    private const int TapsToUnlock = 5;

    protected override void OnMounted()
    {
        State.Theme = ThemePreference.Current;
        State.Manager = Services.GetRequiredService<AccountManager>();
        State.AllowClose = MonthClosePreference.Allowed;
        State.Admin = AdminMode.Enabled;
        base.OnMounted();
    }

    public override VisualNode Render()
    {
        var scheme = MaterialTheme.Current;

        var boxes = new List<VisualNode> { AboutBox(scheme), ThemeBox(scheme), CloseMonthBox(scheme) };
        if (State.Admin)
        {
            boxes.Add(AdminBox(scheme));
        }

        return new ModalAwareContentPage(
            Grid("Auto,*", "*",
                new TopAppBar()
                    .Title("Settings")
                    .Container(scheme.Primary)   // match the home banner
                    .OnContainer(scheme.OnPrimary)
                    .OnBack(() => _ = Navigation?.PopAsync())
                    .GridRow(0),
                ScrollView(
                    VStack([.. boxes]).Spacing(24).Padding(16)
                ).GridRow(1),
                // The import confirm dialog (ADR-0007 ModalHost). Its content is built every render even while
                // closed, so ImportConfirmDialog is null-safe when there is no pending import.
                new ModalHost()
                    .IsOpen(State.ConfirmOpen)
                    .OnDismiss(CloseConfirm)
                    .Content(ImportConfirmDialog(scheme))
                    .GridRowSpan(2),
                RenderSnackbar().GridRow(1)
            )
        ).HasNavigationBar(false);
    }

    // ---- About (version; five quick taps unlock the admin section) ---------------------------------------

    private VisualNode AboutBox(MaterialScheme scheme)
    {
        var rows = new List<VisualNode>
        {
            DetailRow("Version", $"{BuildInfo.Version} ({BuildInfo.Build})", scheme),
        };

        if (BuildInfo.BuildDate is { } built)
        {
            rows.Add(DetailRow("Built", built.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), scheme));
        }

#if DEBUG
        // The commit SHA identifies the exact source of a dev build; release builds omit it (design §9).
        if (BuildInfo.CommitSha is { } sha)
        {
            rows.Add(DetailRow("Commit", sha, scheme));
        }
#endif

        return Section("About", scheme,
            VStack([.. rows]).Spacing(6).Padding(16, 14),
            onTap: RegisterAboutTap
        );
    }

    // Count quick taps on the About box; five within the window (each resets the timer) reveal the admin section.
    private void RegisterAboutTap()
    {
        if (State.Admin)
        {
            return;
        }

        var now = Environment.TickCount64;
        var count = now - State.LastTapTicks <= TapWindowMs ? State.AboutTaps + 1 : 1;

        if (count >= TapsToUnlock)
        {
            AdminMode.Enabled = true;
            SetState(s => { s.Admin = true; s.AboutTaps = 0; s.LastTapTicks = now; });
            _ = ShowSnackbar("Admin mode enabled");
        }
        else
        {
            SetState(s => { s.AboutTaps = count; s.LastTapTicks = now; });
        }
    }

    // One "Label — value" line in the About box: a muted key on the left, the value filling the rest.
    private static VisualNode DetailRow(string label, string value, MaterialScheme scheme) =>
        Grid("Auto", "90,*",
            Label(label)
                .FontSize(14)
                .TextColor(scheme.OnSurfaceVariant)
                .VCenter()
                .GridColumn(0),
            Label(value)
                .FontSize(14)
                .TextColor(scheme.OnSurface)
                .VCenter()
                .GridColumn(1)
        );

    // ---- Theme selector ----------------------------------------------------------------------------------

    private VisualNode ThemeBox(MaterialScheme scheme) =>
        Section("Theme", scheme,
            Grid("Auto", "*,*,*",
                ThemeSegment("System", AppTheme.Unspecified, scheme).GridColumn(0),
                ThemeSegment("Light", AppTheme.Light, scheme).GridColumn(1),
                ThemeSegment("Dark", AppTheme.Dark, scheme).GridColumn(2)
            ).ColumnSpacing(8).Padding(16, 12)
        );

    // One pill in the tristate selector: the active option filled with primaryContainer, the rest a faint
    // surface pill. The box behind is surfaceContainer, so an unselected `surface` pill still reads as distinct.
    private VisualNode ThemeSegment(string label, AppTheme theme, MaterialScheme scheme)
    {
        var selected = State.Theme == theme;
        return Border(
            Label(label)
                .FontSize(13)
                .TextColor(selected ? scheme.OnPrimaryContainer : scheme.OnSurfaceVariant)
                .HCenter()
                .VCenter()
        )
        .BackgroundColor(selected ? scheme.PrimaryContainer : scheme.Surface)
        .StrokeThickness(0)
        .StrokeShape(new RoundRectangle().CornerRadius(20)) // full-height pill
        .HeightRequest(40)
        .OnTapped(() => SelectTheme(theme));
    }

    private void SelectTheme(AppTheme theme)
    {
        ThemePreference.Set(theme);
        SetState(s => s.Theme = theme);
    }

    // ---- Close months (Core month-close) -----------------------------------------------------------------

    // A persisted switch. When on, closing is allowed: closed months collapse into a carried balance and the ledger
    // shows a close button on the oldest open month. When off, past months stay visible but read-only.
    private VisualNode CloseMonthBox(MaterialScheme scheme) =>
        Section("Close months", scheme,
            Grid("Auto", "*,Auto",
                VStack(
                    Label("Allow closing months")
                        .FontSize(15)
                        .TextColor(scheme.OnSurface),
                    Label("Collapse a finished month into a carried balance. A close button appears on the oldest open month.")
                        .FontSize(12)
                        .TextColor(scheme.OnSurfaceVariant)
                ).Spacing(2).VCenter().GridColumn(0),
                Switch()
                    .IsToggled(State.AllowClose)
                    .OnColor(scheme.Primary)
                    .ThumbColor(scheme.OnPrimary)
                    .VCenter()
                    .Margin(12, 0, 0, 0)
                    .OnToggled(() => ToggleAllowClose(!State.AllowClose))
                    .GridColumn(1)
            ).Padding(16, 12)
        );

    // Persist the preference and apply it live: SetIgnoreMonthClosed reloads every account under the new mode
    // (collapsed vs. visible-read-only), then the shell re-renders on return via OnChanged.
    private void ToggleAllowClose(bool allowed)
    {
        MonthClosePreference.Allowed = allowed;
        State.Manager?.SetIgnoreMonthClosed(!allowed);
        Props.OnChanged?.Invoke();
        SetState(s => s.AllowClose = allowed);
    }

    // ---- Admin (raw account export / import, ADR-0008) ---------------------------------------------------

    private VisualNode AdminBox(MaterialScheme scheme) =>
        Section("Admin", scheme,
            Grid("Auto", "*,*",
                AdminButton("Export", ExportAccountFile, scheme).GridColumn(0),
                AdminButton("Import", () => _ = PickAndPreviewImport(), scheme).GridColumn(1)
            ).ColumnSpacing(12).Padding(16, 12)
        );

    // An M3 filled-tonal button (secondaryContainer), full-height pill.
    private static VisualNode AdminButton(string text, Action onClicked, MaterialScheme scheme) =>
        Button(text)
            .BackgroundColor(scheme.SecondaryContainer)
            .TextColor(scheme.OnSecondaryContainer)
            .FontFamily("OpenSansSemibold")
            .FontSize(14)
            .CornerRadius(20)
            .HeightRequest(40)
            .OnClicked(onClicked);

    // Export: the account's raw log lines to a `<id>.jsonl` cache file, then the platform share sheet (which chooses
    // the destination). The id lives in the file name so a later import can route it (ADR-0008).
    private void ExportAccountFile()
    {
        var manager = State.Manager;
        var account = manager?.GetAccounts().FirstOrDefault();
        if (manager is null || account is null)
        {
            return;
        }

        _ = ShareLogAsync(account.Id, manager.ExportAccount(account).ToList());
    }

    private static async Task ShareLogAsync(Guid id, List<string> lines)
    {
        var path = System.IO.Path.Combine(FileSystem.CacheDirectory, $"{id:N}.jsonl");
        await File.WriteAllLinesAsync(path, lines);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Export account",
            File = new ShareFile(path),
        });
    }

    // Import: pick a file, read the account id from its name, validate + summarise, then open the confirm dialog.
    // Nothing is applied here — ConfirmImport does that after the user confirms.
    private async Task PickAndPreviewImport()
    {
        var manager = State.Manager;
        var account = manager?.GetAccounts().FirstOrDefault();
        if (manager is null || account is null)
        {
            return;
        }

        FileResult? file;
        try
        {
            file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a backup (.jsonl)" });
        }
        catch
        {
            _ = ShowSnackbar("Couldn't open the file picker.");
            return;
        }

        if (file is null)
        {
            return; // cancelled
        }

        // The id lives only in the file name (ADR-0008); a renamed file can't be routed.
        if (!Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension(file.FileName), out var backupId))
        {
            _ = ShowSnackbar("The file name must be the account id.");
            return;
        }

        List<string> lines;
        try
        {
            lines = (await File.ReadAllLinesAsync(file.FullPath)).ToList();
        }
        catch
        {
            _ = ShowSnackbar("Couldn't read the file.");
            return;
        }

        ImportPreview preview;
        try
        {
            preview = manager.PreviewImport(lines);
        }
        catch
        {
            _ = ShowSnackbar("Not a valid account log.");
            return;
        }

        SetState(s =>
        {
            s.BackupId = backupId;
            s.ImportLines = lines;
            s.Preview = preview;
            s.Mismatch = backupId != account.Id;
            s.ConfirmOpen = true;
        });
    }

    // The confirm dialog. Built every render (ModalHost.Content is eager), so null-safe while closed.
    private VisualNode ImportConfirmDialog(MaterialScheme scheme)
    {
        if (State.Preview is not { } preview)
        {
            return Grid();
        }

        var recency = preview.LatestDate is { } d ? $", up to {d:d MMM yyyy}" : string.Empty;
        var summary = $"{preview.EventCount} events{recency}.";

        var (title, message) = State.Mismatch
            ? ("Import a different account?",
               $"This backup is from a different account or install (“{preview.Name}” — {summary}) "
               + "Importing replaces this account entirely and adopts the backup. The current data is kept as a "
               + "recoverable deleted account.")
            : ("Replace this account?",
               $"Restore “{preview.Name}” — {summary} This overwrites all current data. A backup is kept.");

        return new AlertDialog()
            .Title(title)
            .Message(message)
            .ConfirmText("Replace")
            .DismissText("Cancel")
            .Destructive(true)
            .OnConfirm(ConfirmImport)
            .OnDismiss(CloseConfirm);
    }

    private void ConfirmImport()
    {
        var manager = State.Manager;
        var account = manager?.GetAccounts().FirstOrDefault();
        if (manager is null || account is null || State.ImportLines is not { } lines)
        {
            CloseConfirm();
            return;
        }

        try
        {
            manager.ImportAccount(account, State.BackupId, lines);
        }
        catch
        {
            SetState(s => { s.ConfirmOpen = false; s.Preview = null; s.ImportLines = null; });
            _ = ShowSnackbar("Import failed.");
            return;
        }

        Props.OnAccountReplaced?.Invoke();
        SetState(s => { s.ConfirmOpen = false; s.Preview = null; s.ImportLines = null; });
        _ = ShowSnackbar("Account imported");
    }

    private void CloseConfirm() =>
        SetState(s => { s.ConfirmOpen = false; s.Preview = null; s.ImportLines = null; });

    // ---- snackbar --------------------------------------------------------------------------------------

    // Show a brief message; a token guards the auto-dismiss so a later snackbar can't be hidden by an earlier timer.
    private async Task ShowSnackbar(string text)
    {
        var token = State.SnackbarToken + 1;
        SetState(s => { s.SnackbarOpen = true; s.SnackbarText = text; s.SnackbarToken = token; });

        await Task.Delay(4000);
        SetState(s =>
        {
            if (s.SnackbarToken == token)
            {
                s.SnackbarOpen = false;
            }
        });
    }

    private VisualNode RenderSnackbar() =>
        State.SnackbarOpen
            ? Grid(new Snackbar().Message(State.SnackbarText)).VEnd()
            : Grid();

    // ---- section box (the rounded surfaceContainer idiom, with a subheader) -------------------------------

    private static VisualNode Section(string header, MaterialScheme scheme, VisualNode content, Action? onTap = null)
    {
        var border = Border(content)
            .BackgroundColor(scheme.SurfaceContainer)
            .StrokeThickness(0)
            .StrokeShape(new RoundRectangle().CornerRadius(16));

        if (onTap is not null)
        {
            border = border.OnTapped(onTap);
        }

        return VStack(
            Label(header)
                .FontSize(14)
                .TextColor(scheme.OnSurfaceVariant)
                .Margin(4, 0, 0, 0),
            border
        ).Spacing(8);
    }
}
