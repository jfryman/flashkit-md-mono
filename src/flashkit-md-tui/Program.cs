using Terminal.Gui.App;

namespace FlashKit.Tui;

static class Program
{
    static void Main()
    {
        Application.Init(null);
        var window = new ProgrammerTuiWindow();
        Application.AddTimeout(TimeSpan.FromSeconds(2), () =>
        {
            _ = window.Model.RefreshAsync();
            return true;
        });
        _ = window.Model.RefreshAsync();
        Application.Run(window, null);
        window.Dispose();
        Application.Shutdown();
    }
}
