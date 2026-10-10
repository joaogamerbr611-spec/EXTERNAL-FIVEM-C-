namespace FiveMExternal;

/// <summary>Owns the attach/reconnect lifecycle. Loads and persists Settings automatically.</summary>
internal sealed class AppHost : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly ProcessMemory memory = new();
    private readonly GameSdk game;
    private readonly CheatController cheat;
    private readonly Settings settings;

    public ImGuiOverlay  Overlay  { get; }
    public Settings      Settings => settings;
    public CheatController Cheat  => cheat;

    public AppHost()
    {
        // load persisted config or fall back to defaults
        settings = Settings.Load();
        game     = new GameSdk(memory);
        cheat    = new CheatController(memory, game, settings);
        Overlay  = new ImGuiOverlay(cheat);
        _ = AttachLoopAsync(cancellation.Token);
    }

    private async Task AttachLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!memory.AttachByWindowClass("grcWindow"))
            {
                await Task.Delay(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
                continue;
            }

            game.Initialize();
            cheat.Start(token);
            try
            {
                while (memory.IsAttached && !token.IsCancellationRequested)
                    await Task.Delay(500, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally
            {
                cheat.Stop();
                // persist settings on every clean detach
                settings.Save();
                memory.Detach();
            }
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        // save on shutdown too, in case FiveM closed hard
        try { settings.Save(); } catch { }
        cheat.Dispose();
        memory.Dispose();
        cancellation.Dispose();
    }
}
