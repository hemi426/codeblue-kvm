using LibVLCSharp.Shared;

namespace CodeBlue.Viewer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Core.Initialize();
        Application.Run(new MainForm());
    }
}
