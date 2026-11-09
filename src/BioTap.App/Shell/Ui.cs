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
}
