namespace FiveMExternal;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var host = new AppHost();
        host.Overlay.Run().GetAwaiter().GetResult();
    }
}
