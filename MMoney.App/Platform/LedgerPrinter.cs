namespace MMoney.App.Platform;

/// <summary>
/// Prints an HTML statement through the platform's print system. On Android it loads the HTML into an off-screen
/// <c>WebView</c> and hands the WebView's print document adapter to the framework's <c>PrintManager</c>, which
/// raises the native print dialog (printer selection, page setup, and "Save as PDF"). A no-op on other targets.
/// </summary>
public static class LedgerPrinter
{
#if ANDROID
    // The WebView must outlive the Print() call: the framework pulls pages from its document adapter lazily, and a
    // garbage-collected WebView cancels the job. Held here (one at a time, replaced by the next print) until the job
    // is handed over. Uses the Activity context a WebView requires; released implicitly when the next print starts.
    private static Android.Webkit.WebView? _printView;
#endif

    /// <summary>Renders <paramref name="html"/> and starts a print job named <paramref name="jobName"/>.</summary>
    public static void Print(string html, string jobName)
    {
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is not { } activity)
        {
            return;
        }

        var webView = new Android.Webkit.WebView(activity);
        webView.SetWebViewClient(new PrintReadyClient(jobName));
        _printView = webView;
        // Print only once the content has laid out (OnPageFinished); base URL null — the document is self-contained.
        webView.LoadDataWithBaseURL(null, html, "text/html", "UTF-8", null);
#endif
    }

#if ANDROID
    // Waits for the WebView to finish loading, then hands its print adapter to the framework.
    private sealed class PrintReadyClient(string jobName) : Android.Webkit.WebViewClient
    {
        public override void OnPageFinished(Android.Webkit.WebView? view, string? url)
        {
            base.OnPageFinished(view, url);

            if (view is null
                || Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is not { } activity
                || activity.GetSystemService(Android.Content.Context.PrintService) is not Android.Print.PrintManager printManager)
            {
                return;
            }

            var adapter = view.CreatePrintDocumentAdapter(jobName);
            printManager.Print(jobName, adapter, new Android.Print.PrintAttributes.Builder().Build());
        }
    }
#endif
}
