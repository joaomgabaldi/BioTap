namespace BioTap.App.Shell;

/// <summary>
/// Entry points from XAML events and native callbacks. An exception escaping one is a fail-fast in WinUI even when
/// Application.UnhandledException marks it handled (seen in practice), so every handler body goes through here.
/// </summary>
static class Ui
{
    public static void Run(Action body, string what)
    {
        try { body(); }
        catch (Exception e) { Program.Log.Write("ERROR", $"{what}: {e}"); }
    }

    public static async void RunAsync(Func<Task> body, string what)
    {
        try { await body(); }
        catch (Exception e) { Program.Log.Write("ERROR", $"{what}: {e}"); }
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the n-th next XAML frame. A window resize or show takes effect at once
    /// but XAML draws the change with the next frame: frame 1 draws what was just set, frame 2 comes after it.
    /// </summary>
    public static void AfterFrames(int n, Action action)
    {
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            if (--n > 0) return;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler;
            Run(action, "after frames");
        };
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += handler;
    }
}
