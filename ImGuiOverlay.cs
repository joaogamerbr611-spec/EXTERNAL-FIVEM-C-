using ClickableTransparentOverlay;
using ImGuiNET;
using System.Numerics;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using DrawingSize = System.Drawing.Size;
using Forms = System.Windows.Forms;

namespace FiveMExternal;

/// <summary>DirectX 11 Dear ImGui overlay — ESP, Radar, Crosshair, menu completo.</summary>
public sealed unsafe class ImGuiOverlay : Overlay
{
    private readonly CheatController cheat;
    private readonly DrawingSize viewport;
    private readonly Dictionary<nint, FadeMotion> entityFade = new();
    private readonly List<BackgroundParticle> backgroundParticles = new();
    private ImFontPtr espFont;
    private ImFontPtr titleFont;
    private readonly IntPtr[] navIconTextures = new IntPtr[5];
    private bool menuKeyWasDown;
    private bool espKeyWasDown;
    private bool unlockVehicleKeyWasDown;
    private bool fixVehicleKeyWasDown;
    private bool streamModeInitialized;
    private bool streamModeApplied;
    private bool hoverBindHotkeyHeld;
    private bool waitingForBindActivatorRelease;
    private string hoverBindTarget = string.Empty;
    private long hoverBindStartedAt;
    private int  selectedTab;
    private nint selectedPlayer;
    private nint selectedVehicle;
    private string vehicleActionStatus = string.Empty;
    private string teleportStatus = string.Empty;
    private TeleportChoice? selectedTeleport;
    private string savedTeleportName = string.Empty;
    private string licenseKey = string.Empty;
    private string loginStatus = string.Empty;
    private bool loginAccepted;
    private bool hideLicenseKey = true;
    private bool loginMinimized;
    private bool rememberLicenseKey;
    private long loadingStartedAt;

    private readonly record struct TeleportChoice(string Name, Vector3 Position, bool IsWaypoint);

    private static readonly (string Name, Vector3 Position)[] TeleportPresets =
    {
        ("Praça Legion", new Vector3(215.8f, -810.1f, 30.7f)),
        ("Praia Del Perro", new Vector3(-1605.3f, -1077.0f, 13.0f)),
        ("Aeroporto LS", new Vector3(-1034.6f, -2733.6f, 20.2f)),
        ("Sandy Shores", new Vector3(1703.0f, 3292.0f, 41.1f)),
        ("Paleto Bay", new Vector3(-102.2f, 6450.3f, 31.5f)),
        ("Monte Chiliad", new Vector3(501.4f, 5604.7f, 797.9f))
    };

    // Lilás secundário extraído do estado selecionado dos teleportes (#6951DD).
    private static readonly Vector4 SecondaryColor = new(.412f, .318f, .867f, 1f);
    private static readonly Vector4 SecondaryHoverColor = new(.50f, .42f, .96f, 1f);
    private const uint SecondaryColorU32 = 0xFFDD5169u;
    private const uint SecondaryHoverColorU32 = 0xFFF56B80u;

    // keybind picker state
    private bool   pickingKey;
    private string pickingTarget = string.Empty;  // which setting we're picking for

    private sealed class FadeMotion { public float Opacity; public long LastFrameAt; }
    private readonly record struct BackgroundParticle(Vector2 Position, float Radius, float Drift, float Phase);

    public ImGuiOverlay(CheatController cheat) : base(GetPrimaryWidth(), GetPrimaryHeight())
    {
        this.cheat = cheat;
        viewport   = new DrawingSize(GetPrimaryWidth(), GetPrimaryHeight());
        rememberLicenseKey = cheat.Settings.RememberLicenseKey;
        licenseKey = rememberLicenseKey ? cheat.Settings.SavedLicenseKey ?? string.Empty : string.Empty;
        loginAccepted = false;
        FPSLimit   = 60;
    }

    private static int GetPrimaryWidth()  => Forms.Screen.PrimaryScreen?.Bounds.Width  ?? 1920;
    private static int GetPrimaryHeight() => Forms.Screen.PrimaryScreen?.Bounds.Height ?? 1080;

    // ─── click-through helper ────────────────────────────────────────────────
    // ClickableTransparentOverlay 11.x does not expose IsClickable directly.
    // We flip WS_EX_TRANSPARENT on the overlay HWND ourselves.
    private void SetClickable(bool clickable)
    {
        var hwnd = FindOverlayHwnd();
        if (hwnd == IntPtr.Zero) return;
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        style = clickable
            ? style & ~NativeMethods.WS_EX_TRANSPARENT
            : style | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, style);
    }

    private IntPtr FindOverlayHwnd()
    {
        // Overlay keeps its Win32Window private. Resolve that exact instance
        // instead of enumerating the process' windows, which could otherwise
        // select a hidden or unrelated window.
        var windowField = typeof(Overlay).GetField("window", BindingFlags.Instance | BindingFlags.NonPublic);
        var overlayWindow = windowField?.GetValue(this);
        var handleField = overlayWindow?.GetType().GetField("Handle", BindingFlags.Instance | BindingFlags.Public);
        if (handleField?.GetValue(overlayWindow) is IntPtr handle && handle != IntPtr.Zero) return handle;

        // Fallback for a future package version that changes its private layout.
        var pid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        var found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var wPid);
            if (wPid == pid) { found = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // Same capture-affinity approach used by the Executor project. The native
    // state is only changed when the persisted setting changes.
    private void ApplyStreamMode(bool enabled)
    {
        if (streamModeInitialized && streamModeApplied == enabled) return;

        var hwnd = FindOverlayHwnd();
        if (hwnd == IntPtr.Zero) return;

        var affinity = enabled ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE;
        if (!NativeMethods.GetWindowDisplayAffinity(hwnd, out var currentAffinity)) return;
        if (currentAffinity == affinity || NativeMethods.SetWindowDisplayAffinity(hwnd, affinity))
        {
            streamModeApplied = enabled;
            streamModeInitialized = true;
        }
    }

    protected override Task PostInitialized()
    {
        ReplaceFont(cfg =>
        {
            // Match the native VZX ESP typography instead of the lighter UI
            // weight used by the original managed overlay.
            espFont = LoadProjectFont("InterSemiBold", new ImFontConfigPtr(cfg), 16f);
            titleFont = LoadProjectFont("InterBold", new ImFontConfigPtr(cfg), 16f);
        });
        LoadNavigationIcons();
        CreateBackgroundParticles();
        SetClickable(!loginAccepted || cheat.Settings.ShowMenu);
        return Task.CompletedTask;
    }

    private void LoadNavigationIcons()
    {
        var names = new[] { "crosshair.png", "eye.png", "players.png", "vehicle.png", "settings.png" };
        for (var index = 0; index < names.Length; index++)
        {
            var resourceName = $"assets.nav.{names[index]}";
            using var stream = OpenEmbeddedAsset(resourceName);
            if (stream is null) continue;
            using var icon = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
            // The DirectX overlay uses a point sampler for texture images. Resize
            // once with a high-quality filter so the 512 px sources stay smooth
            // at their final 24 px navigation size.
            icon.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new SixLabors.ImageSharp.Size(24, 24),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Lanczos3
            }));
            icon.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < rows.Height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        if (row[x].A == 0) continue;
                        row[x].R = 255;
                        row[x].G = 255;
                        row[x].B = 255;
                    }
                }
            });
            AddOrGetImagePointer($"embedded://{resourceName}", icon, true, out navIconTextures[index]);
        }
    }

    private ImFontPtr LoadProjectFont(string fontName, ImFontConfigPtr config, float sizePixels)
    {
        using var stream = OpenEmbeddedAsset("assets.fonts.FontInter.hpp");
        if (stream is null) return default;
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();
        var sizeMatch = Regex.Match(source, $@"{fontName}_compressed_size\s*=\s*(\d+)");
        var dataMatch = Regex.Match(source, $@"{fontName}_compressed_data\s*\[[^\]]+\]\s*=\s*\{{(?<data>.*?)\}};", RegexOptions.Singleline);
        if (!sizeMatch.Success || !dataMatch.Success) return default;
        var hexValues = Regex.Matches(dataMatch.Groups["data"].Value, @"0x[0-9a-fA-F]+");
        var data = new uint[hexValues.Count];
        for (var i = 0; i < data.Length; i++)
            data[i] = uint.Parse(hexValues[i].Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        fixed (uint* pointer = data)
            return ImGui.GetIO().Fonts.AddFontFromMemoryCompressedTTF(
                (IntPtr)pointer, int.Parse(sizeMatch.Groups[1].Value, CultureInfo.InvariantCulture), sizePixels, config);
    }

    private static Stream? OpenEmbeddedAsset(string suffix)
    {
        var assembly = typeof(ImGuiOverlay).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        return resourceName is null ? null : assembly.GetManifestResourceStream(resourceName);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Main render loop
    // ──────────────────────────────────────────────────────────────────────────
    protected override void Render()
    {
        var s = cheat.Settings;
        ApplyStreamMode(s.StreamMode);

        if (!loginAccepted)
        {
            var loginInsertDown = NativeMethods.IsKeyDown(NativeMethods.VK_INSERT);
            if (loginInsertDown && !menuKeyWasDown) loginMinimized = !loginMinimized;
            menuKeyWasDown = loginInsertDown;
            if (loginMinimized)
            {
                SetClickable(false);
                return;
            }
            SetClickable(true);
            RenderLogin();
            return;
        }

        if (loadingStartedAt != 0)
        {
            var elapsed = Environment.TickCount64 - loadingStartedAt;
            if (elapsed < 1700)
            {
                SetClickable(false);
                RenderLoading(Math.Clamp(elapsed / 1700f, 0f, 1f));
                return;
            }

            loadingStartedAt = 0;
            SetClickable(s.ShowMenu);
        }
        if (!NativeMethods.IsKeyDown(0x48))
        {
            hoverBindHotkeyHeld = false;
            hoverBindTarget = string.Empty;
            hoverBindStartedAt = 0;
        }

        // INSERT toggles menu
        var insertDown = NativeMethods.IsKeyDown(NativeMethods.VK_INSERT);
        if (insertDown && !menuKeyWasDown)
        {
            s.ShowMenu = !s.ShowMenu;
            SetClickable(s.ShowMenu);
        }
        menuKeyWasDown = insertDown;

        // keybind picker — capture next key press
        if (pickingKey) HandleKeyPick(s);
        else ProcessQuickBinds(s);

        var drawList = ImGui.GetBackgroundDrawList();

        // FOV circles
        var center = new Vector2(viewport.Width * .5f, viewport.Height * .5f);
        if (s.AimEnabled    && s.AimShowFov)    drawList.AddCircle(center, s.AimFov,    0xCFFFFFFF, 96, 1f);
        if (s.SilentEnabled && s.SilentShowFov) drawList.AddCircle(center, s.SilentFov, 0xCFAF78FF, 96, 1f);
        if (s.TriggerEnabled)                   drawList.AddCircle(center, s.TriggerFov, 0xCF3399FF, 96, 1f);

        // Crosshair
        if (s.ShowCrosshair) RenderCrosshair(drawList, center, s);

        // ESP
        if (s.EspEnabled && cheat.TryGetViewMatrix(out var view))
        {
            var local = cheat.LocalPosition;
            foreach (var entity in cheat.Entities) RenderEntity(drawList, view, entity, local);
            if (s.EspVehicles) foreach (var vehicle in cheat.Vehicles) RenderVehicle(drawList, view, vehicle, local);
        }

        // Status bar (top-right)
        RenderStatusBar(drawList, s);

        // Menu
        if (s.ShowMenu)
        {
            RenderMenu();
        }
    }

    private void CreateBackgroundParticles()
    {
        if (backgroundParticles.Count != 0) return;
        var random = new Random(0x565A);
        for (var index = 0; index < 42; index++)
        {
            var position = new Vector2(
                random.NextSingle() * viewport.Width,
                random.NextSingle() * viewport.Height);
            backgroundParticles.Add(new BackgroundParticle(
                position,
                1.2f + random.NextSingle() * 1.8f,
                .22f + random.NextSingle() * .55f,
                random.NextSingle() * MathF.Tau));
        }
    }

    private void RenderBackgroundParticles()
    {
        if (backgroundParticles.Count == 0) CreateBackgroundParticles();
        var draw = ImGui.GetBackgroundDrawList();
        var time = Environment.TickCount64 / 1000f;
        foreach (var particle in backgroundParticles)
        {
            var x = particle.Position.X + MathF.Sin(time * particle.Drift + particle.Phase) * 18f;
            var y = particle.Position.Y + MathF.Cos(time * (particle.Drift * .7f) + particle.Phase) * 10f;
            var position = new Vector2(x, y);
            var pulse = .45f + .55f * ((MathF.Sin(time * 1.3f + particle.Phase) + 1f) * .5f);
            var alpha = (uint)(28f + pulse * 48f);
            var color = 0x00DD5169u | (alpha << 24);
            draw.AddCircleFilled(position, particle.Radius, color, 10);
            if (particle.Radius > 2.2f)
                draw.AddCircle(position, particle.Radius + 2.4f, (color & 0x00FFFFFFu) | ((alpha / 3u) << 24), 10, .7f);
        }
    }

    private void RenderMenuBackgroundParticles()
    {
        if (backgroundParticles.Count == 0) CreateBackgroundParticles();
        var draw = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var clipMin = windowPos + new Vector2(8f, 8f);
        var clipMax = windowPos + windowSize - new Vector2(8f, 8f);
        var time = Environment.TickCount64 / 1000f;
        draw.PushClipRect(clipMin, clipMax, true);
        foreach (var particle in backgroundParticles)
        {
            var x = windowPos.X + (particle.Position.X / viewport.Width) * windowSize.X + MathF.Sin(time * particle.Drift + particle.Phase) * 8f;
            var y = windowPos.Y + (particle.Position.Y / viewport.Height) * windowSize.Y + MathF.Cos(time * particle.Drift + particle.Phase) * 6f;
            var pulse = .4f + .6f * ((MathF.Sin(time * 1.2f + particle.Phase) + 1f) * .5f);
            var alpha = (uint)(20f + pulse * 34f);
            var color = 0x00DD5169u | (alpha << 24);
            draw.AddCircleFilled(new Vector2(x, y), particle.Radius, color, 10);
        }
        draw.PopClipRect();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Crosshair
    // ──────────────────────────────────────────────────────────────────────────
    private static void RenderCrosshair(ImDrawListPtr draw, Vector2 center, Settings s)
    {
        var col = ImGui.ColorConvertFloat4ToU32(s.CrosshairColor);
        var sz  = s.CrosshairSize;
        var gap = s.CrosshairGap;
        var t   = s.CrosshairThickness;

        switch (s.CrosshairStyle)
        {
            case 0: // classic plus
                draw.AddLine(new Vector2(center.X - sz - gap, center.Y), new Vector2(center.X - gap, center.Y), col, t);
                draw.AddLine(new Vector2(center.X + gap, center.Y), new Vector2(center.X + sz + gap, center.Y), col, t);
                draw.AddLine(new Vector2(center.X, center.Y - sz - gap), new Vector2(center.X, center.Y - gap), col, t);
                draw.AddLine(new Vector2(center.X, center.Y + gap), new Vector2(center.X, center.Y + sz + gap), col, t);
                break;
            case 1: // dot
                draw.AddCircleFilled(center, t + 1.5f, col);
                break;
            case 2: // circle only
                draw.AddCircle(center, sz, col, 48, t);
                break;
            case 3: // cross + dot
                draw.AddLine(new Vector2(center.X - sz - gap, center.Y), new Vector2(center.X - gap, center.Y), col, t);
                draw.AddLine(new Vector2(center.X + gap, center.Y), new Vector2(center.X + sz + gap, center.Y), col, t);
                draw.AddLine(new Vector2(center.X, center.Y - sz - gap), new Vector2(center.X, center.Y - gap), col, t);
                draw.AddLine(new Vector2(center.X, center.Y + gap), new Vector2(center.X, center.Y + sz + gap), col, t);
                draw.AddCircleFilled(center, t + 1f, col);
                break;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Status bar / Watermark (HUD)
    // ──────────────────────────────────────────────────────────────────────────
    private void RenderStatusBar(ImDrawListPtr draw, Settings s)
    {
        if (!s.ShowStatusBar) return;

        var attached = cheat.LocalPed != 0;
        var statusColor = attached ? 0xFF44D668u : 0xFFE04343u;
        var statusText  = attached ? "VZX CONECTADO" : "VZX AGUARDANDO FIVEM...";

        var padding = new Vector2(10, 5);
        var textSize = ImGui.CalcTextSize(statusText);
        var barW = textSize.X + padding.X * 2 + 10;
        var barH = textSize.Y + padding.Y * 2;

        // Position calculation
        float x0, y0;
        switch (s.StatusBarPosition)
        {
            case 0: // Top-Left (Clear from FiveM street/compass HUD)
                x0 = 16f;
                y0 = 36f;
                break;
            case 1: // Bottom-Left (Above radar/map)
                x0 = 16f;
                y0 = viewport.Height - barH - 220f;
                break;
            case 2: // Bottom-Right
                x0 = viewport.Width - barW - 16f;
                y0 = viewport.Height - barH - 80f;
                break;
            case 3: // Top-Right (Standard)
            default:
                x0 = viewport.Width - barW - 16f;
                y0 = 16f;
                break;
        }

        draw.AddRectFilled(new Vector2(x0, y0), new Vector2(x0 + barW, y0 + barH), 0xD0101520u, 5f);
        draw.AddRect(new Vector2(x0, y0), new Vector2(x0 + barW, y0 + barH), 0xFF35261Cu, 5f, ImDrawFlags.None, 1f);
        draw.AddCircleFilled(new Vector2(x0 + padding.X, y0 + barH * .5f), 3.5f, statusColor);
        draw.AddText(new Vector2(x0 + padding.X + 8, y0 + padding.Y), 0xFFE0E0E0u, statusText);

        // Feature flags strip below
        var indicators = new (string Label, bool Active)[]
        {
            ("ESP",     s.EspEnabled),
            ("AIM",     s.AimEnabled),
            ("SILENT",  s.SilentEnabled),
            ("TRIG",    s.TriggerEnabled),
            ("GOD",     s.GodMode),
        };

        var ix = x0;
        var iy = y0 + barH + 4;
        foreach (var (label, active) in indicators)
        {
            var sz = ImGui.CalcTextSize(label);
            var bg = active ? 0xD01E2E18u : 0xD0151922u;
            var fg = active ? 0xFF66FF88u : 0x80B8A698u;
            var border = active ? 0xFF44AA66u : 0xFF2A2016u;
            draw.AddRectFilled(new Vector2(ix, iy), new Vector2(ix + sz.X + 8, iy + sz.Y + 4), bg, 3f);
            draw.AddRect(new Vector2(ix, iy), new Vector2(ix + sz.X + 8, iy + sz.Y + 4), border, 3f, ImDrawFlags.None, 1f);
            draw.AddText(new Vector2(ix + 4, iy + 2), fg, label);
            ix += sz.X + 12;
            if (ix > viewport.Width - 16) { ix = x0; iy += sz.Y + 6; }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Keybind picker
    // ──────────────────────────────────────────────────────────────────────────
    private void HandleKeyPick(Settings s)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 9f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14f, 12f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(.065f, .065f, .075f, .99f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(.20f, .20f, .23f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(.16f, .16f, .18f, 1f));
        ImGui.OpenPopup("##keypick");
        ImGui.SetNextWindowPos(new Vector2(viewport.Width * .5f - 145f, viewport.Height * .5f - 58f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(290f, 0f), ImGuiCond.Appearing);
        if (ImGui.BeginPopupModal("##keypick", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
        {
            var draw = ImGui.GetWindowDrawList();
            var top = ImGui.GetCursorScreenPos();
            draw.AddRectFilled(top, new Vector2(top.X + ImGui.GetContentRegionAvail().X, top.Y + 2f), 0xFF96969Au, 1f);
            ImGui.Dummy(new Vector2(0f, 4f));
            if (titleFont.NativePtr != null) ImGui.PushFont(titleFont);
            ImGui.TextColored(new Vector4(.78f, .79f, .82f, 1f), "DEFINIR ATALHO");
            if (titleFont.NativePtr != null) ImGui.PopFont();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(.67f, .68f, .72f, 1f), waitingForBindActivatorRelease ? "Solte H e pressione a nova tecla" : "Pressione a nova tecla");
            ImGui.Dummy(new Vector2(0f, 5f));
            ImGui.TextDisabled("Delete ou Backspace  |  remover");
            ImGui.SameLine();
            ImGui.TextDisabled("ESC  |  cancelar");
            ImGui.EndPopup();
        }
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(3);

        // H abre este seletor e não pode ser gravado como tecla da função.
        if (waitingForBindActivatorRelease)
        {
            if (NativeMethods.IsKeyDown(0x48)) return;
            waitingForBindActivatorRelease = false;
        }

        // scan all vkeys 1-254
        for (var vk = 1; vk < 255; vk++)
        {
            if (!NativeMethods.IsKeyDown(vk)) continue;
            if (vk == 0x1B) { pickingKey = false; pickingTarget = string.Empty; waitingForBindActivatorRelease = false; return; } // ESC = cancel
            ApplyPickedKey(s, vk is NativeMethods.VK_DELETE or 0x08 ? 0 : vk);
            pickingKey    = false;
            pickingTarget = string.Empty;
            waitingForBindActivatorRelease = false;
            return;
        }
    }

    private void ApplyPickedKey(Settings s, int vk)
    {
        switch (pickingTarget)
        {
            case "aim":     s.AimKey     = vk; break;
            case "silent":  s.SilentKey  = vk; break;
            case "trigger": s.TriggerKey = vk; break;
            case "esp":
                s.EspToggleKey = vk;
                espKeyWasDown = NativeMethods.IsKeyDown(vk);
                break;
            case "unlock":
                s.VehicleUnlockKey = vk;
                unlockVehicleKeyWasDown = NativeMethods.IsKeyDown(vk);
                break;
            case "fix":
                s.VehicleFixKey = vk;
                fixVehicleKeyWasDown = NativeMethods.IsKeyDown(vk);
                break;
        }
    }

    private static bool IsPressedOnce(int key, ref bool wasDown)
    {
        var isDown = key != 0 && NativeMethods.IsKeyDown(key);
        var pressed = isDown && !wasDown;
        wasDown = isDown;
        return pressed;
    }

    private void ProcessQuickBinds(Settings s)
    {
        if (IsPressedOnce(s.EspToggleKey, ref espKeyWasDown))
            s.EspEnabled = !s.EspEnabled;

        if (IsPressedOnce(s.VehicleUnlockKey, ref unlockVehicleKeyWasDown))
            vehicleActionStatus = cheat.TryUnlockNearestVehicle() ? "Veículo mais próximo destrancado." : "Nenhum veículo próximo para destrancar.";

        if (IsPressedOnce(s.VehicleFixKey, ref fixVehicleKeyWasDown))
            vehicleActionStatus = cheat.TryFixNearestVehicle() ? "Veículo mais próximo reparado." : "Nenhum veículo próximo para reparar.";

    }

    private void OpenHoverBind(string target, bool hovered)
    {
        if (!hovered) return;
        var holdingH = NativeMethods.IsKeyDown(0x48);
        if (!holdingH)
        {
            ImGui.SetTooltip("Segure H por 30 ms para configurar o atalho");
            return;
        }

        if (hoverBindTarget != target)
        {
            hoverBindTarget = target;
            hoverBindStartedAt = Environment.TickCount64;
        }

        var elapsed = Environment.TickCount64 - hoverBindStartedAt;
        ImGui.SetTooltip($"Segure H por 30 ms ({Math.Min(elapsed, 30)} ms / 30 ms)");
        if (pickingKey || hoverBindHotkeyHeld || elapsed < 30) return;

        pickingKey = true;
        pickingTarget = target;
        waitingForBindActivatorRelease = true;
        hoverBindHotkeyHeld = true;
    }

    private static float BindPreviewWidth(int key) => key == 0 ? 0f : ImGui.CalcTextSize($"[{(Forms.Keys)key}]").X + 14f;

    private static void DrawBindPreview(ImDrawListPtr draw, Vector2 position, int key, float height = 20f)
    {
        if (key == 0) return;
        var text = $"[{(Forms.Keys)key}]";
        var width = BindPreviewWidth(key);
        draw.AddRectFilled(position, position + new Vector2(width, height), 0xFF202024u, 4f);
        draw.AddRect(position, position + new Vector2(width, height), 0xFF303036u, 4f, ImDrawFlags.None, 1f);
        draw.AddText(position + new Vector2(7f, 2f), 0xFFB8B8BEu, text);
    }

    private bool ActionButton(string label, string bindTarget, int bindKey)
    {
        var available = ImGui.GetContentRegionAvail().X;
        var previewWidth = BindPreviewWidth(bindKey);
        var buttonWidth = previewWidth > 0f ? available - previewWidth - 6f : available;
        var clicked = ImGui.Button(label, new Vector2(buttonWidth, 26f));
        OpenHoverBind(bindTarget, ImGui.IsItemHovered());
        if (previewWidth > 0f)
        {
            ImGui.SameLine(0, 6f);
            var position = ImGui.GetCursorScreenPos();
            DrawBindPreview(ImGui.GetWindowDrawList(), position, bindKey, 26f);
            ImGui.Dummy(new Vector2(previewWidth, 26f));
        }
        return clicked;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Menu
    // ──────────────────────────────────────────────────────────────────────────
    private void RenderLoading(float progress)
    {
        const float cardWidth = 410f;
        const float cardHeight = 176f;
        ImGui.SetNextWindowPos(new Vector2((viewport.Width - cardWidth) * .5f, (viewport.Height - cardHeight) * .5f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(cardWidth, cardHeight), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(24f, 22f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        PushPalette();

        ImGui.Begin("##vzx_loading", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs);
        RenderMenuBackgroundParticles();
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        draw.AddRectFilledMultiColor(origin, origin + new Vector2(size.X, 3f), SecondaryColorU32, 0x00DD5169u, 0x00DD5169u, SecondaryColorU32);

        if (titleFont.NativePtr != null) ImGui.PushFont(titleFont);
        ImGui.TextColored(SecondaryColor, "VZX");
        ImGui.SameLine(0f, 6f);
        ImGui.TextColored(new Vector4(.77f, .78f, .82f, 1f), "FIVEM EXTERNAL");
        if (titleFont.NativePtr != null) ImGui.PopFont();
        ImGui.Dummy(new Vector2(0f, 12f));
        ImGui.TextColored(new Vector4(.84f, .85f, .89f, 1f), "Preparando o executor");
        ImGui.TextDisabled("Carregando interface e recursos...");
        ImGui.Dummy(new Vector2(0f, 10f));

        var barMin = ImGui.GetCursorScreenPos();
        var barWidth = ImGui.GetContentRegionAvail().X;
        const float barHeight = 8f;
        draw.AddRectFilled(barMin, barMin + new Vector2(barWidth, barHeight), 0xFF1B1B20u, 4f);
        draw.AddRect(barMin, barMin + new Vector2(barWidth, barHeight), 0xFF34303Fu, 4f, ImDrawFlags.None, 1f);
        if (progress > 0f)
            draw.AddRectFilled(barMin, barMin + new Vector2(barWidth * progress, barHeight), SecondaryColorU32, 4f);
        ImGui.Dummy(new Vector2(barWidth, barHeight + 8f));
        ImGui.TextDisabled($"{progress * 100f:0}%");

        ImGui.End();
        ImGui.PopStyleColor(25);
        ImGui.PopStyleVar(3);
    }

    private void RenderLogin()
    {
        const float cardWidth = 470f;
        const float cardHeight = 310f;
        ImGui.SetNextWindowPos(new Vector2((viewport.Width - cardWidth) * .5f, (viewport.Height - cardHeight) * .5f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(cardWidth, cardHeight), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(24f, 22f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        PushPalette();

        ImGui.Begin("##vzx_login", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        RenderMenuBackgroundParticles();
        var draw = ImGui.GetWindowDrawList();
        var origin = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var accentMin = origin + new Vector2(10f, 4f);
        var accentMax = origin + new Vector2(size.X - 10f, 7f);
        draw.AddRectFilledMultiColor(accentMin, accentMax, SecondaryColorU32, 0x00DD5169u, 0x00DD5169u, SecondaryColorU32);

        if (titleFont.NativePtr != null) ImGui.PushFont(titleFont);
        ImGui.TextColored(SecondaryColor, "VZX");
        ImGui.SameLine(0f, 6f);
        ImGui.TextColored(new Vector4(.77f, .78f, .82f, 1f), "FIVEM EXTERNAL");
        if (titleFont.NativePtr != null) ImGui.PopFont();
        RenderLoginControls();
        ImGui.TextDisabled("Acesso seguro ao executor");
        ImGui.Dummy(new Vector2(0f, 10f));

        SectionTitle("AUTENTICAÇÃO");
        ImGui.TextColored(new Vector4(.78f, .79f, .83f, 1f), "Chave de licença");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 82f);
        var flags = hideLicenseKey ? ImGuiInputTextFlags.Password : ImGuiInputTextFlags.None;
        ImGui.InputText("##license_key", ref licenseKey, 128, flags);
        ImGui.SameLine(0f, 6f);
        if (ImGui.Button(hideLicenseKey ? "EXIBIR" : "OCULTAR", new Vector2(-1, 0f))) hideLicenseKey = !hideLicenseKey;
        ImGui.TextDisabled("Cole sua key para liberar o acesso.");
        ImGui.Checkbox("Salvar key neste computador", ref rememberLicenseKey);
        ImGui.Dummy(new Vector2(0f, 6f));

        ImGui.PushStyleColor(ImGuiCol.Button, SecondaryColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, SecondaryHoverColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.33f, .24f, .70f, 1f));
        if (ImGui.Button("ENTRAR NO EXECUTOR", new Vector2(-1, 32f)))
        {
            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                loginStatus = "Informe uma chave de licença para continuar.";
            }
            else
            {
                cheat.Settings.RememberLicenseKey = rememberLicenseKey;
                cheat.Settings.SavedLicenseKey = rememberLicenseKey ? licenseKey.Trim() : string.Empty;
                cheat.Settings.ShowMenu = true;
                cheat.Settings.Save();
                loginAccepted = true;
                loadingStartedAt = Environment.TickCount64;
            }
        }
        ImGui.PopStyleColor(3);

        if (!string.IsNullOrEmpty(loginStatus))
        {
            ImGui.Dummy(new Vector2(0f, 3f));
            ImGui.TextColored(new Vector4(.95f, .48f, .40f, 1f), loginStatus);
        }
        else
        {
            ImGui.Dummy(new Vector2(0f, 3f));
            ImGui.TextDisabled("• Pronto para conectar");
        }

        ImGui.End();
        ImGui.PopStyleColor(25);
        ImGui.PopStyleVar(4);
    }

    private void RenderLoginControls()
    {
        var draw = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        var mouse = ImGui.GetIO().MousePos;
        const float buttonSize = 24f;
        const float gap = 6f;
        var closeMin = new Vector2(windowPos.X + windowSize.X - 18f - buttonSize, windowPos.Y + 14f);
        var minMin = closeMin - new Vector2(buttonSize + gap, 0f);
        var minMax = minMin + new Vector2(buttonSize);
        var closeMax = closeMin + new Vector2(buttonSize);

        void DrawButton(Vector2 min, Vector2 max, bool hovered)
        {
            draw.AddRectFilled(min, max, hovered ? 0xFF292431u : 0xFF18181Cu, 5f);
            draw.AddRect(min, max, hovered ? SecondaryColorU32 : 0xFF303036u, 5f, ImDrawFlags.None, 1f);
        }

        var minHovered = mouse.X >= minMin.X && mouse.X <= minMax.X && mouse.Y >= minMin.Y && mouse.Y <= minMax.Y;
        DrawButton(minMin, minMax, minHovered);
        draw.AddLine(new Vector2(minMin.X + 7f, minMin.Y + 12f), new Vector2(minMax.X - 7f, minMin.Y + 12f), minHovered ? 0xFFFFFFFFu : 0xFFA0A0A0u, 1.6f);
        if (minHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            loginMinimized = true;
            SetClickable(false);
        }

        var closeHovered = mouse.X >= closeMin.X && mouse.X <= closeMax.X && mouse.Y >= closeMin.Y && mouse.Y <= closeMax.Y;
        DrawButton(closeMin, closeMax, closeHovered);
        var closeColor = closeHovered ? 0xFFFFFFFFu : 0xFFA0A0A0u;
        draw.AddLine(closeMin + new Vector2(7f, 7f), closeMax - new Vector2(7f, 7f), closeColor, 1.6f);
        draw.AddLine(new Vector2(closeMax.X - 7f, closeMin.Y + 7f), new Vector2(closeMin.X + 7f, closeMax.Y - 7f), closeColor, 1.6f);
        if (closeHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) Environment.Exit(0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Menu
    // ──────────────────────────────────────────────────────────────────────────
    // ──────────────────────────────────────────────────────────────────────────
    //  Menu
    // ──────────────────────────────────────────────────────────────────────────
    // ──────────────────────────────────────────────────────────────────────────
    //  Menu
    // ──────────────────────────────────────────────────────────────────────────
    private void RenderMenu()
    {
        ImGui.SetNextWindowPos(new Vector2(viewport.Width * .5f - 380, viewport.Height * .5f - 245), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(760, 490), ImGuiCond.FirstUseEver);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 12));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 6));

        PushPalette();
        ImGui.Begin("##vzx_in_game_menu", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize);
        RenderMenuBackgroundParticles();

        // Compact application header.
        if (titleFont.NativePtr != null) ImGui.PushFont(titleFont);
        ImGui.TextColored(SecondaryColor, "VZX");
        if (titleFont.NativePtr != null) ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(.55f, .56f, .60f, 1f), "FIVEM EXTERNAL");

        // Window Control Buttons (Minimize & Close)
        RenderHeaderControls();

        ImGui.Separator();

        // Sidebar keeps the navigation visible while the controls use the rest of
        // the window. This removes the oversized tab strip and shortens scanning.
        ImGui.BeginChild("menu_navigation", new Vector2(54, -1), ImGuiChildFlags.Borders);
        ImGui.Dummy(new Vector2(0, 4));
        var tabs = new[] { ("VISUAL", 0), ("AIM", 1), ("TELEPORTES", 2), ("VEÍCULOS", 3), ("JOGADORES", 4), ("CONFIG", 5) };
        foreach (var tab in tabs)
            if (SideNavButton(tab.Item1, tab.Item2)) selectedTab = tab.Item2;
        ImGui.EndChild();

        ImGui.SameLine(0, 8f);
        ImGui.BeginChild("menu_content", new Vector2(0, -1), ImGuiChildFlags.None);
        var pageTitle = tabs[selectedTab].Item1;
        if (titleFont.NativePtr != null) ImGui.PushFont(titleFont);
        ImGui.TextColored(new Vector4(.72f, .73f, .77f, 1f), pageTitle);
        if (titleFont.NativePtr != null) ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextDisabled("Configurações em tempo real");
        ImGui.Separator();

        switch (selectedTab)
        {
            case 0: DrawVisualMenu();     break;
            case 1: DrawAimbotUnified();  break;
            case 2: DrawMovementMenu();   break;
            case 3: DrawVehicleUnified(); break;
            case 4: DrawPlayerListMenu(); break;
            case 5: DrawConfigMiscMenu(); break;
        }
        ImGui.EndChild();

        ImGui.End();
        ImGui.PopStyleColor(25);
        ImGui.PopStyleVar(9);
    }

    private void RenderHeaderControls()
    {
        var btnW = 28f;
        var btnH = 22f;
        var gap = 6f;
        var totalW = btnW * 2f + gap;

        var availW = ImGui.GetContentRegionAvail().X;
        var startX = ImGui.GetCursorScreenPos().X + availW - totalW;
        var startY = ImGui.GetCursorScreenPos().Y - 26f;

        var draw = ImGui.GetWindowDrawList();
        var io = ImGui.GetIO();
        var mouse = io.MousePos;

        // ── Minimize Button (-) ──────────────────────────────────────────────
        var minPos = new Vector2(startX, startY);
        var minMax = new Vector2(startX + btnW, startY + btnH);
        var minHovered = mouse.X >= minPos.X && mouse.X <= minMax.X && mouse.Y >= minPos.Y && mouse.Y <= minMax.Y;

        var minBgCol = minHovered ? 0xF02A2A2Au : 0xD0181818u;
        var minBorderCol = minHovered ? 0xFF4D4D4Du : 0xFF2D2D2Du;
        draw.AddRectFilled(minPos, minMax, minBgCol, 5f);
        draw.AddRect(minPos, minMax, minBorderCol, 5f, ImDrawFlags.None, 1f);

        // Horizontal dash for minimize icon
        var dashCol = minHovered ? 0xFFFFFFFFu : 0xFFA0A0A0u;
        draw.AddLine(new Vector2(minPos.X + 8f, startY + btnH * 0.5f), new Vector2(minMax.X - 8f, startY + btnH * 0.5f), dashCol, 1.8f);

        if (minHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            cheat.Settings.ShowMenu = false;
            SetClickable(false);
        }

        // ── Close Button (X) ────────────────────────────────────────────────
        var closePos = new Vector2(startX + btnW + gap, startY);
        var closeMax = new Vector2(startX + btnW * 2f + gap, startY + btnH);
        var closeHovered = mouse.X >= closePos.X && mouse.X <= closeMax.X && mouse.Y >= closePos.Y && mouse.Y <= closeMax.Y;

        var closeBgCol = closeHovered ? 0xFF2A28DCu : 0xD0181818u; // Red on hover, stealth dark gray default
        var closeBorderCol = closeHovered ? 0xFF4A44FFu : 0xFF2D2D2Du;
        draw.AddRectFilled(closePos, closeMax, closeBgCol, 5f);
        draw.AddRect(closePos, closeMax, closeBorderCol, 5f, ImDrawFlags.None, 1f);

        // 'X' icon
        var xCol = closeHovered ? 0xFFFFFFFFu : 0xFFA0A0A0u;
        var pad = 8f;
        draw.AddLine(new Vector2(closePos.X + pad, closePos.Y + 6f), new Vector2(closeMax.X - pad, closeMax.Y - 6f), xCol, 1.8f);
        draw.AddLine(new Vector2(closeMax.X - pad, closePos.Y + 6f), new Vector2(closePos.X + pad, closeMax.Y - 6f), xCol, 1.8f);

        if (closeHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            try { cheat.Settings.Save(); } catch { }
            Environment.Exit(0);
        }
    }

    private bool SideNavButton(string tooltip, int tab)
    {
        var isActive = selectedTab == tab;
        var position = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetContentRegionAvail().X, 38f);
        var clicked = ImGui.InvisibleButton($"##nav_{tab}", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        var background = isActive ? 0xCCDD5169u : hovered ? 0xFF1B1B1Eu : 0x00121214u;
        var foreground = isActive ? 0xFFFFFFFFu : hovered ? 0xFFD3D5D9u : 0xFF85878Du;
        var center = position + size * .5f;
        // Keep the active state a true square regardless of the child padding.
        var visualMin = center - new Vector2(17f);
        var visualMax = center + new Vector2(17f);
        draw.AddRectFilled(visualMin, visualMax, background, 3f);
        if (isActive)
        {
            draw.AddRect(visualMin, visualMax, SecondaryColorU32, 3f, ImDrawFlags.None, 1f);
        }
        var icon = GetNavigationIcon(tab);
        if (icon != IntPtr.Zero)
            draw.AddImage(icon, center - new Vector2(10f), center + new Vector2(10f), Vector2.Zero, Vector2.One, foreground);
        else DrawNavigationIcon(draw, tab, center, foreground);
        if (hovered) ImGui.SetTooltip(tooltip);
        return clicked;
    }

    private IntPtr GetNavigationIcon(int tab) => tab switch
    {
        0 => navIconTextures[1], // eye
        1 => navIconTextures[0], // crosshair
        3 => navIconTextures[3], // vehicle
        4 => navIconTextures[2], // players
        5 => navIconTextures[4], // settings
        _ => IntPtr.Zero          // movement stays as a direction glyph
    };

    // Consistent monochrome outline icon set; each glyph occupies the same 24 px grid.
    private static void DrawNavigationIcon(ImDrawListPtr draw, int tab, Vector2 center, uint color)
    {
        const float thin = 1.5f;
        switch (tab)
        {
            case 0: // eye / visual
                draw.AddBezierCubic(new Vector2(center.X - 12, center.Y), new Vector2(center.X - 6, center.Y - 8), new Vector2(center.X + 6, center.Y - 8), new Vector2(center.X + 12, center.Y), color, thin);
                draw.AddBezierCubic(new Vector2(center.X - 12, center.Y), new Vector2(center.X - 6, center.Y + 8), new Vector2(center.X + 6, center.Y + 8), new Vector2(center.X + 12, center.Y), color, thin);
                draw.AddCircleFilled(center, 3f, color);
                break;
            case 1: // crosshair from the supplied reference style
                draw.AddCircle(center, 8f, color, 24, thin);
                draw.AddCircle(center, 2f, color, 12, thin);
                draw.AddLine(new Vector2(center.X - 12, center.Y), new Vector2(center.X - 6, center.Y), color, thin);
                draw.AddLine(new Vector2(center.X + 6, center.Y), new Vector2(center.X + 12, center.Y), color, thin);
                draw.AddLine(new Vector2(center.X, center.Y - 12), new Vector2(center.X, center.Y - 6), color, thin);
                draw.AddLine(new Vector2(center.X, center.Y + 6), new Vector2(center.X, center.Y + 12), color, thin);
                break;
            case 2: // movement
                draw.AddLine(new Vector2(center.X, center.Y + 11), new Vector2(center.X, center.Y - 11), color, thin);
                draw.AddLine(new Vector2(center.X, center.Y - 11), new Vector2(center.X - 4, center.Y - 6), color, thin);
                draw.AddLine(new Vector2(center.X, center.Y - 11), new Vector2(center.X + 4, center.Y - 6), color, thin);
                draw.AddLine(new Vector2(center.X - 9, center.Y + 5), new Vector2(center.X + 9, center.Y + 5), color, thin);
                draw.AddLine(new Vector2(center.X - 9, center.Y + 5), new Vector2(center.X - 4, center.Y + 1), color, thin);
                draw.AddLine(new Vector2(center.X + 9, center.Y + 5), new Vector2(center.X + 4, center.Y + 1), color, thin);
                break;
            case 3: // vehicle
                draw.AddRect(new Vector2(center.X - 11, center.Y - 5), new Vector2(center.X + 11, center.Y + 6), color, 3f, ImDrawFlags.None, thin);
                draw.AddLine(new Vector2(center.X - 7, center.Y - 5), new Vector2(center.X - 3, center.Y - 10), color, thin);
                draw.AddLine(new Vector2(center.X - 3, center.Y - 10), new Vector2(center.X + 6, center.Y - 10), color, thin);
                draw.AddLine(new Vector2(center.X + 6, center.Y - 10), new Vector2(center.X + 9, center.Y - 5), color, thin);
                draw.AddCircleFilled(new Vector2(center.X - 6, center.Y + 6), 2.5f, color);
                draw.AddCircleFilled(new Vector2(center.X + 6, center.Y + 6), 2.5f, color);
                break;
            case 4: // players
                draw.AddCircle(new Vector2(center.X - 4, center.Y - 5), 3.5f, color, 12, thin);
                draw.AddCircle(new Vector2(center.X + 6, center.Y - 4), 3f, color, 12, thin);
                draw.AddBezierCubic(new Vector2(center.X - 11, center.Y + 10), new Vector2(center.X - 9, center.Y + 2), new Vector2(center.X + 1, center.Y + 2), new Vector2(center.X + 3, center.Y + 10), color, thin);
                draw.AddLine(new Vector2(center.X + 3, center.Y + 10), new Vector2(center.X + 12, center.Y + 10), color, thin);
                break;
            default: // configuration sliders
                draw.AddLine(new Vector2(center.X - 11, center.Y - 7), new Vector2(center.X + 11, center.Y - 7), color, thin);
                draw.AddLine(new Vector2(center.X - 11, center.Y), new Vector2(center.X + 11, center.Y), color, thin);
                draw.AddLine(new Vector2(center.X - 11, center.Y + 7), new Vector2(center.X + 11, center.Y + 7), color, thin);
                draw.AddCircleFilled(new Vector2(center.X - 4, center.Y - 7), 2.5f, color);
                draw.AddCircleFilled(new Vector2(center.X + 5, center.Y), 2.5f, color);
                draw.AddCircleFilled(new Vector2(center.X - 1, center.Y + 7), 2.5f, color);
                break;
        }
    }

    private void PushPalette()
    {
        // Original black and graphite palette.
        ImGui.PushStyleColor(ImGuiCol.WindowBg,         new Vector4(.055f, .055f, .06f, .975f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg,          new Vector4(.07f, .07f, .078f, .94f));
        ImGui.PushStyleColor(ImGuiCol.Border,           new Vector4(.13f, .13f, .145f, .72f));
        ImGui.PushStyleColor(ImGuiCol.Separator,        new Vector4(.12f, .12f, .13f, .62f));
        ImGui.PushStyleColor(ImGuiCol.Text,             new Vector4(.73f, .74f, .77f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TextDisabled,     new Vector4(.39f, .40f, .43f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg,          new Vector4(.05f, .05f, .055f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive,    new Vector4(.05f, .05f, .055f, 1f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, new Vector4(.05f, .05f, .055f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button,           new Vector4(.12f, .12f, .14f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered,    new Vector4(.15f, .15f, .17f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,     new Vector4(.19f, .19f, .22f, 1f));
        // Frames for inputs, combos and selections
        ImGui.PushStyleColor(ImGuiCol.FrameBg,          new Vector4(.09f, .09f, .10f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered,   new Vector4(.115f, .115f, .13f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive,    new Vector4(.14f, .14f, .16f, 1f));
        // Checkmark & Sliders
        ImGui.PushStyleColor(ImGuiCol.CheckMark,        SecondaryColor);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab,       SecondaryColor);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, SecondaryHoverColor);
        ImGui.PushStyleColor(ImGuiCol.Header,           new Vector4(.25f, .20f, .48f, .85f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered,    new Vector4(.32f, .26f, .62f, .92f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive,     SecondaryColor);
        ImGui.PushStyleColor(ImGuiCol.PopupBg,          new Vector4(.07f, .07f, .08f, .98f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg,      new Vector4(.05f, .05f, .06f, .5f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab,    new Vector4(.20f, .21f, .24f, .8f));
        ImGui.PushStyleColor(ImGuiCol.NavCursor,        Vector4.Zero);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Tab: VISUAL (ESP)
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawVisualMenu()
    {
        var s = cheat.Settings;
        var avail = ImGui.GetContentRegionAvail();
        var colW = (avail.X - 12f) * 0.5f;

        ImGui.BeginChild("visual_left", new Vector2(colW, avail.Y), ImGuiChildFlags.Borders);
        SectionTitle("ESP GERAL");
        Toggle("Ativar ESP Geral",             () => s.EspEnabled,        v => s.EspEnabled = v, "esp", s.EspToggleKey);
        Toggle("Estilo Executor / Ref.",       () => s.EspReferenceStyle, v => s.EspReferenceStyle = v);

        ImGui.Spacing();
        SectionTitle("ELEMENTOS VISUAIS");
        Toggle("Caixa Delimitadora (Box)",     () => s.EspBox,            v => s.EspBox = v);
        Toggle("Marcador na Cabeça (+)",       () => s.EspHeadCross,      v => s.EspHeadCross = v);
        Toggle("Exibir Nome do Ped",           () => s.EspName,           v => s.EspName = v);
        Toggle("Exibir Distância em Metros",   () => s.EspDistance,       v => s.EspDistance = v);
        Toggle("Exibir Arma do Ped",           () => s.EspWeapon,         v => s.EspWeapon = v);
        Toggle("Barra Dinâmica de Vida",       () => s.EspHealth,         v => s.EspHealth = v);
        Toggle("Esqueleto Completo (Skeleton)",() => s.EspSkeleton,       v => s.EspSkeleton = v);

        ImGui.Spacing();
        SectionTitle("DISTÂNCIA E ESPESSURA");
        var espDist = (int)s.EspMaxDistance; if (ModernSliderInt("Distância Máxima", ref espDist, 25, 2000, "{0} m")) s.EspMaxDistance = espDist;
        var txtSz   = s.EspTextSize;         if (ModernSliderFloat("Tamanho da Fonte", ref txtSz, 9f, 20f, "{0:0} px")) s.EspTextSize = txtSz;
        var boxT    = s.EspBoxThickness;     if (ModernSliderFloat("Espessura da Box", ref boxT, .75f, 4f, "{0:0.1} px")) s.EspBoxThickness = boxT;
        var skelT   = s.EspSkeletonThickness;if (ModernSliderFloat("Espessura Bones", ref skelT, .5f, 4f, "{0:0.1} px")) s.EspSkeletonThickness = skelT;
        ImGui.EndChild();

        ImGui.SameLine(0, 12f);
        ImGui.BeginChild("visual_right", new Vector2(colW, avail.Y), ImGuiChildFlags.Borders);
        SectionTitle("PALETA DE CORES");
        EditColor("Jogadores Online",          s.PlayerColor,          v => s.PlayerColor = v);
        EditColor("NPCs / Pedestres",          s.NpcColor,             v => s.NpcColor = v);
        EditColor("Esqueleto (Bones)",         s.SkeletonColor,        v => s.SkeletonColor = v);
        EditColor("Barra de Vida (Executor)",  s.ReferenceHealthColor, v => s.ReferenceHealthColor = v);

        ImGui.Spacing();
        SectionTitle("TELEMETRIA DE RENDER");
        var localPos = cheat.LocalPosition;
        InfoRow("Jogadores Válidos", $"{cheat.Entities.Count(e => e.IsPlayer)}");
        InfoRow("NPCs no Stream", $"{cheat.Entities.Count(e => !e.IsPlayer)}");
        InfoRow("Veículos Próximos", $"{cheat.Vehicles.Length}");
        InfoRow("Coordenadas X, Y", $"{localPos.X:F1}, {localPos.Y:F1}");
        InfoRow("Altitude (Z)", $"{localPos.Z:F1} m");
        ImGui.EndChild();
    }

    private void SectionTitle(string title)
    {
        var draw = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        var width = MathF.Max(80f, ImGui.GetContentRegionAvail().X);
        const float headerHeight = 24f;
        var headerEnd = cursor + new Vector2(width, headerHeight);

        // Compact card header: secondary lilac fades into the graphite panel.
        draw.AddRectFilledMultiColor(cursor, headerEnd, 0x72DD5169u, 0x16131317u, 0x16131317u, 0x72DD5169u);
        draw.AddRect(cursor, headerEnd, 0x8ADD5169u, 4f, ImDrawFlags.None, 1f);
        draw.AddRectFilled(cursor + new Vector2(0f, 4f), cursor + new Vector2(3f, headerHeight - 4f), SecondaryColorU32, 1.5f);

        var titlePosition = cursor + new Vector2(10f, 4f);
        const uint titleColor = 0xFFF5D6D6u;
        if (titleFont.NativePtr != null) draw.AddText(titleFont, 16f, titlePosition, titleColor, title);
        else draw.AddText(titlePosition, titleColor, title);

        // Register the header as an item instead of moving the cursor outside
        // a child window; this keeps ImGui's parent-boundary assertion valid.
        ImGui.Dummy(new Vector2(width, headerHeight + 5f));
    }

    private static bool ModernSliderInt(string label, ref int value, int min, int max, string format)
    {
        var draw = ImGui.GetWindowDrawList();
        var availW = ImGui.GetContentRegionAvail().X;

        ImGui.TextColored(new Vector4(.61f, .62f, .66f, 1f), label);
        ImGui.SameLine();
        var valStr = string.Format(format, value);
        var valW = ImGui.CalcTextSize(valStr).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - valW);
        ImGui.TextColored(new Vector4(.74f, .75f, .78f, 1f), valStr);

        var trackH = 8f;
        var cursor = ImGui.GetCursorScreenPos();
        ImGui.PushID(label);
        var changed = false;
        var active = ImGui.InvisibleButton("##slider", new Vector2(availW, 14f));
        if (ImGui.IsItemActive())
        {
            var mouseX = ImGui.GetIO().MousePos.X;
            var t = Math.Clamp((mouseX - cursor.X) / availW, 0f, 1f);
            var newVal = (int)Math.Round(min + t * (max - min));
            if (newVal != value) { value = newVal; changed = true; }
        }
        ImGui.PopID();

        var trackY = cursor.Y + 3f;
        var ratio = Math.Clamp((float)(value - min) / (max - min), 0f, 1f);
        var grabX = cursor.X + ratio * availW;

        // Background track (Deep Charcoal Black: #141416 -> ABGR 0xE0161414u)
        draw.AddRectFilled(new Vector2(cursor.X, trackY), new Vector2(cursor.X + availW, trackY + trackH), 0xE0161414u, 4f);
        draw.AddRect(new Vector2(cursor.X, trackY), new Vector2(cursor.X + availW, trackY + trackH), 0xFF2A2A2Eu, 4f, ImDrawFlags.None, 1f);

        // Filled progress track (Sleek Steel Gray: #4B4D54 -> ABGR 0xFF544D4Bu)
        if (ratio > 0.01f)
            draw.AddRectFilled(new Vector2(cursor.X, trackY), new Vector2(grabX, trackY + trackH), SecondaryColorU32, 4f);

        // Grab circle (Silver Core + White rim: ABGR 0xFFCCCCCCu)
        draw.AddCircleFilled(new Vector2(grabX, trackY + trackH * 0.5f), 6f, SecondaryColorU32);
        draw.AddCircle(new Vector2(grabX, trackY + trackH * 0.5f), 6f, SecondaryHoverColorU32, 16, 1f);

        ImGui.Spacing();
        return changed;
    }

    private static bool ModernSliderFloat(string label, ref float value, float min, float max, string format)
    {
        var draw = ImGui.GetWindowDrawList();
        var availW = ImGui.GetContentRegionAvail().X;

        ImGui.TextColored(new Vector4(.61f, .62f, .66f, 1f), label);
        ImGui.SameLine();
        var valStr = string.Format(format, value);
        var valW = ImGui.CalcTextSize(valStr).X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - valW);
        ImGui.TextColored(new Vector4(.74f, .75f, .78f, 1f), valStr);

        var trackH = 8f;
        var cursor = ImGui.GetCursorScreenPos();
        ImGui.PushID(label);
        var changed = false;
        var active = ImGui.InvisibleButton("##slider", new Vector2(availW, 14f));
        if (ImGui.IsItemActive())
        {
            var mouseX = ImGui.GetIO().MousePos.X;
            var t = Math.Clamp((mouseX - cursor.X) / availW, 0f, 1f);
            var newVal = min + t * (max - min);
            if (Math.Abs(newVal - value) > 0.01f) { value = newVal; changed = true; }
        }
        ImGui.PopID();

        var trackY = cursor.Y + 3f;
        var ratio = Math.Clamp((value - min) / (max - min), 0f, 1f);
        var grabX = cursor.X + ratio * availW;

        // Background track (Deep Charcoal Black: #141416 -> ABGR 0xE0161414u)
        draw.AddRectFilled(new Vector2(cursor.X, trackY), new Vector2(cursor.X + availW, trackY + trackH), 0xE0161414u, 4f);
        draw.AddRect(new Vector2(cursor.X, trackY), new Vector2(cursor.X + availW, trackY + trackH), 0xFF2A2A2Eu, 4f, ImDrawFlags.None, 1f);

        // Filled progress track (Sleek Steel Gray: #4B4D54 -> ABGR 0xFF544D4Bu)
        if (ratio > 0.01f)
            draw.AddRectFilled(new Vector2(cursor.X, trackY), new Vector2(grabX, trackY + trackH), SecondaryColorU32, 4f);

        // Grab circle (Silver Core + White rim: ABGR 0xFFCCCCCCu)
        draw.AddCircleFilled(new Vector2(grabX, trackY + trackH * 0.5f), 6f, SecondaryColorU32);
        draw.AddCircle(new Vector2(grabX, trackY + trackH * 0.5f), 6f, SecondaryHoverColorU32, 16, 1f);

        ImGui.Spacing();
        return changed;
    }

    private static void InfoRow(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine();
        var valW = ImGui.CalcTextSize(value).X;
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > valW) ImGui.SameLine(ImGui.GetCursorPosX() + avail - valW);
        ImGui.TextColored(new Vector4(.67f, .68f, .72f, 1f), value);
    }

    private void Toggle(string label, Func<bool> get, Action<bool> set, string? bindTarget = null, int bindKey = 0)
    {
        var active = get();
        var draw = ImGui.GetWindowDrawList();
        var cursor = ImGui.GetCursorScreenPos();
        var availW = ImGui.GetContentRegionAvail().X;
        var boxSize = 17f;
        var lineHeight = 22f;

        // Invisible button to capture full row interaction
        ImGui.PushID(label);
        var clicked = ImGui.InvisibleButton("##toggle_row", new Vector2(availW, lineHeight));
        if (clicked) set(!active);
        var hovered = ImGui.IsItemHovered();
        ImGui.PopID();
        if (bindTarget is not null) OpenHoverBind(bindTarget, hovered);

        // Calculate positions
        var boxY = cursor.Y + (lineHeight - boxSize) * 0.5f;
        var boxMin = new Vector2(cursor.X, boxY);
        var boxMax = new Vector2(cursor.X + boxSize, boxY + boxSize);

        // Checkbox Background & Border
        if (active)
        {
            var bgCol = hovered ? SecondaryHoverColorU32 : SecondaryColorU32;
            var borderCol = hovered ? 0xFFFFFFFFu : SecondaryHoverColorU32;
            draw.AddRectFilled(boxMin, boxMax, bgCol, 4f);
            draw.AddRect(boxMin, boxMax, borderCol, 4f, ImDrawFlags.None, 1f);

            var p1 = new Vector2(boxMin.X + 3.8f, boxMin.Y + 8.8f);
            var p2 = new Vector2(boxMin.X + 7.2f, boxMin.Y + 12.4f);
            var p3 = new Vector2(boxMin.X + 13.2f, boxMin.Y + 4.8f);
            draw.AddLine(p1, p2, 0xFFFFFFFFu, 1.6f);
            draw.AddLine(p2, p3, 0xFFFFFFFFu, 1.6f);
        }
        else
        {
            var bgCol = hovered ? 0xF019191Bu : 0xD0121214u;
            var borderCol = hovered ? 0xFF3A3A3Eu : 0xFF25252Au;
            draw.AddRectFilled(boxMin, boxMax, bgCol, 4f);
            draw.AddRect(boxMin, boxMax, borderCol, 4f, ImDrawFlags.None, 1f);
        }

        // Label text
        var textPos = new Vector2(cursor.X + boxSize + 10f, cursor.Y + 2.5f);
        var textCol = active ? 0xFFD0D0D4u : (hovered ? 0xFFC0C0C5u : 0xFF89898Fu);
        var visibleLabel = label.Split("##", StringSplitOptions.None)[0];
        draw.AddText(textPos, textCol, visibleLabel);
        if (bindKey != 0)
        {
            var width = BindPreviewWidth(bindKey);
            DrawBindPreview(draw, new Vector2(cursor.X + availW - width, cursor.Y + 1f), bindKey);
        }
    }

    private static void EditColor(string label, Vector4 color, Action<Vector4> set)
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var textSize = ImGui.CalcTextSize(label);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(new Vector4(.66f, .67f, .71f, 1f), label);

        // Right-align color picker preview
        var colW = 32f;
        var targetX = ImGui.GetCursorPosX() + avail - colW;
        if (targetX > textSize.X + 20) ImGui.SameLine(targetX);
        else ImGui.SameLine();

        var v = color;
        ImGui.PushID(label);
        if (ImGui.ColorEdit4("##picker", ref v, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
            set(v);
        ImGui.PopID();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Tab: AIMBOT (Assist, Silent & Triggerbot)
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawAimbotUnified()
    {
        var s = cheat.Settings;
        var avail = ImGui.GetContentRegionAvail();
        var colW = (avail.X - 12f) * 0.5f;

        ImGui.BeginChild("aim_left", new Vector2(colW, avail.Y), ImGuiChildFlags.Borders);
        SectionTitle("AIM ASSIST (CAMERA)");
        Toggle("Ativar Aim Assist",      () => s.AimEnabled,            v => s.AimEnabled = v, "aim", s.AimKey);
        Toggle("Exibir Círculo FOV",     () => s.AimShowFov,            v => s.AimShowFov = v);
        Toggle("Somente Jogadores",      () => s.AimOnlyPlayers,        v => s.AimOnlyPlayers = v);
        Toggle("Ignorar Invisíveis",     () => s.AimIgnoreInvisible,    v => s.AimIgnoreInvisible = v);
        Toggle("Predição de Velocidade", () => s.AimPrediction,         v => s.AimPrediction = v);
        Toggle("Sempre Ativo",           () => s.AimAlwaysActive,       v => s.AimAlwaysActive = v);
        Toggle("Priorizar Alvo Próximo", () => s.AimPrioritizeDistance, v => s.AimPrioritizeDistance = v);

        ImGui.Spacing();
        SectionTitle("ALVO E ATALHO");
        var bones = new[] { "Cabeça", "Pescoço", "Torso", "Mão Esquerda", "Mão Direita", "Pé Esquerdo", "Pé Direito" };
        var aimB  = s.AimBone; if (ImGui.Combo("Osso Alvo##aim", ref aimB, bones, bones.Length)) s.AimBone = aimB;
        ImGui.Spacing();
        SectionTitle("AJUSTES DE MIRA");
        var fov  = (int)s.AimFov;         if (ModernSliderInt("FOV Aim Assist", ref fov, 20, 500, "{0} px")) s.AimFov = fov;
        var smt  = (int)s.AimSmooth;      if (ModernSliderInt("Suavização (Smooth)", ref smt, 1, 50, "{0}")) s.AimSmooth = smt;
        var dist = (int)s.AimMaxDistance; if (ModernSliderInt("Distância Máxima", ref dist, 10, 1000, "{0} m")) s.AimMaxDistance = dist;
        ImGui.EndChild();

        ImGui.SameLine(0, 12f);
        ImGui.BeginChild("aim_right", new Vector2(colW, avail.Y), ImGuiChildFlags.Borders);
        SectionTitle("SILENT AIM");
        Toggle("Ativar Silent Aim",        () => s.SilentEnabled,            v => s.SilentEnabled = v, "silent", s.SilentKey);
        Toggle("Exibir FOV Silent",        () => s.SilentShowFov,            v => s.SilentShowFov = v);
        Toggle("Somente Jogadores##sil",   () => s.SilentOnlyPlayers,        v => s.SilentOnlyPlayers = v);
        Toggle("Ignorar Invisíveis##sil",  () => s.SilentIgnoreInvisible,    v => s.SilentIgnoreInvisible = v);
        Toggle("Priorizar Próximo##sil",   () => s.SilentPrioritizeDistance, v => s.SilentPrioritizeDistance = v);

        ImGui.Spacing();
        SectionTitle("ALVO E ATALHO");
        var sBones = new[] { "Cabeça", "Torso", "Pé Direito", "Pé Esquerdo", "Mão Direita", "Mão Esquerda", "Pescoço" };
        var sb = s.SilentBone; if (ImGui.Combo("Osso Alvo##sil", ref sb, sBones, sBones.Length)) s.SilentBone = sb;
        ImGui.Spacing();
        SectionTitle("AJUSTES SILENT");
        var sFov  = (int)s.SilentFov;        if (ModernSliderInt("FOV Silent", ref sFov, 20, 500, "{0} px")) s.SilentFov = sFov;
        var sDist = (int)s.SilentMaxDistance;if (ModernSliderInt("Distância Máxima##sil", ref sDist, 10, 1000, "{0} m")) s.SilentMaxDistance = sDist;

        ImGui.Spacing();
        SectionTitle("TRIGGERBOT");
        Toggle("Ativar Triggerbot",        () => s.TriggerEnabled,         v => s.TriggerEnabled = v, "trigger", s.TriggerKey);
        Toggle("Somente Jogadores##trig",  () => s.TriggerOnlyPlayers,     v => s.TriggerOnlyPlayers = v);
        Toggle("Ignorar Invisíveis##trig", () => s.TriggerIgnoreInvisible, v => s.TriggerIgnoreInvisible = v);
        var tFov  = (int)s.TriggerFov;         if (ModernSliderInt("FOV Trigger", ref tFov, 5, 150, "{0} px")) s.TriggerFov = tFov;
        var tDist = (int)s.TriggerMaxDistance; if (ModernSliderInt("Distância Máxima##trig", ref tDist, 10, 500, "{0} m")) s.TriggerMaxDistance = tDist;
        var dmin = s.TriggerDelayMin;          if (ModernSliderInt("Delay Mínimo", ref dmin, 0, 200, "{0} ms")) s.TriggerDelayMin = dmin;
        var dmax = s.TriggerDelayMax;          if (ModernSliderInt("Delay Máximo", ref dmax, 0, 400, "{0} ms")) s.TriggerDelayMax = dmax;
        ImGui.EndChild();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Tab: TELEPORTS
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawMovementMenu()
    {
        var s = cheat.Settings;
        s.SavedTeleports ??= new List<SavedTeleport>();
        var avail = ImGui.GetContentRegionAvail();
        var colW = (avail.X - 12f) * 0.5f;

        ImGui.BeginChild("teleport_left", new Vector2(colW, avail.Y), ImGuiChildFlags.Borders);
        SectionTitle("DESTINO SELECIONADO");
        if (selectedTeleport is { } choice)
        {
            ImGui.Text(choice.Name);
            ImGui.TextDisabled(choice.IsWaypoint ? "Waypoint marcado no mapa" : $"X: {choice.Position.X:0.0}  Y: {choice.Position.Y:0.0}  Z: {choice.Position.Z:0.0}");
            ImGui.Spacing();
            if (ImGui.Button("TELEPORTAR PARA O DESTINO", new Vector2(-1, 30f)))
            {
                var success = choice.IsWaypoint
                    ? cheat.TryTeleportToWaypoint()
                    : cheat.TryTeleportTo(choice.Position);
                teleportStatus = success
                    ? $"Teleporte solicitado: {choice.Name}."
                    : choice.IsWaypoint
                        ? "Waypoint não encontrado ou escrita bloqueada."
                        : "Falha ao aplicar o teleporte.";
            }
        }
        else
        {
            ImGui.TextDisabled("Selecione um destino na lista ao lado.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        SectionTitle("SALVAR POSIÇÃO ATUAL");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##saved_teleport_name", ref savedTeleportName, 40);
        ImGui.TextDisabled("Nome do local (opcional)");
        if (ImGui.Button("SALVAR TELEPORTE ATUAL", new Vector2(-1, 28f)))
        {
            var position = cheat.LocalPosition;
            if (position == Vector3.Zero || !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            {
                teleportStatus = "Não foi possível ler sua posição atual.";
            }
            else
            {
                var name = string.IsNullOrWhiteSpace(savedTeleportName)
                    ? $"Local {s.SavedTeleports.Count + 1}"
                    : savedTeleportName.Trim();
                s.SavedTeleports.Add(new SavedTeleport { Name = name, X = position.X, Y = position.Y, Z = position.Z });
                s.Save();
                savedTeleportName = string.Empty;
                teleportStatus = $"Local salvo: {name}.";
            }
        }
        if (!string.IsNullOrEmpty(teleportStatus))
        {
            ImGui.Spacing();
            var success = teleportStatus.StartsWith("Teleporte") || teleportStatus.StartsWith("Local salvo");
            ImGui.TextColored(success ? new Vector4(.30f, .95f, .55f, 1f) : new Vector4(.95f, .50f, .38f, 1f), teleportStatus);
        }
        ImGui.EndChild();

        ImGui.SameLine(0, 12f);
        ImGui.BeginChild("teleport_right", new Vector2(colW, avail.Y), ImGuiChildFlags.Borders);
        SectionTitle("DESTINOS RÁPIDOS");
        if (TeleportListButton("Waypoint do mapa", "teleport_waypoint", selectedTeleport is { IsWaypoint: true }))
            selectedTeleport = new TeleportChoice("Waypoint do mapa", Vector3.Zero, true);

        for (var index = 0; index < TeleportPresets.Length; index++)
        {
            var destination = TeleportPresets[index];
            var selected = selectedTeleport is { } current && !current.IsWaypoint && current.Name == destination.Name && current.Position == destination.Position;
            if (TeleportListButton(destination.Name, $"teleport_preset_{index}", selected))
                selectedTeleport = new TeleportChoice(destination.Name, destination.Position, false);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        SectionTitle("MEUS TELEPORTES");
        ImGui.BeginChild("saved_teleport_list", new Vector2(-1, -1), ImGuiChildFlags.Borders);
        if (s.SavedTeleports.Count == 0)
        {
            ImGui.TextDisabled("Nenhum local personalizado salvo.");
        }
        else
        {
            var removeIndex = -1;
            for (var index = 0; index < s.SavedTeleports.Count; index++)
            {
                var saved = s.SavedTeleports[index];
                var position = new Vector3(saved.X, saved.Y, saved.Z);
                var selected = selectedTeleport is { } current && !current.IsWaypoint && current.Name == saved.Name && current.Position == position;
                ImGui.PushID(index);
                if (TeleportListButton(saved.Name, "saved_teleport", selected, 34f))
                    selectedTeleport = new TeleportChoice(saved.Name, position, false);
                ImGui.SameLine();
                if (ImGui.SmallButton("X")) removeIndex = index;
                ImGui.PopID();
            }
            if (removeIndex >= 0)
            {
                var removed = s.SavedTeleports[removeIndex];
                s.SavedTeleports.RemoveAt(removeIndex);
                s.Save();
                if (selectedTeleport is { } current && !current.IsWaypoint && current.Name == removed.Name && current.Position == new Vector3(removed.X, removed.Y, removed.Z))
                    selectedTeleport = null;
                teleportStatus = $"Local removido: {removed.Name}.";
            }
        }
        ImGui.EndChild();
        ImGui.EndChild();
    }

    private static bool TeleportListButton(string label, string id, bool selected, float rightReserved = 0f)
    {
        var width = MathF.Max(80f, ImGui.GetContentRegionAvail().X - rightReserved);
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, SecondaryColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, SecondaryHoverColor);
        }

        var clicked = ImGui.Button($"{label}##{id}", new Vector2(width, 25f));
        if (selected) ImGui.PopStyleColor(2);
        return clicked;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Tab: VEHICLES (ESP, Ações Rápidas & Lista)
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawVehicleUnified()
    {
        var s = cheat.Settings;
        var avail = ImGui.GetContentRegionAvail();
        var colW = (avail.X - 12f) * 0.5f;

        ImGui.BeginChild("vehicle_left", new Vector2(colW, avail.Y), ImGuiChildFlags.Borders);
        SectionTitle("VEHICLE ESP");
        Toggle("Ativar ESP de Veículos", () => s.EspVehicles,        v => s.EspVehicles = v);
        Toggle("Nome do Modelo",         () => s.EspVehicleName,     v => s.EspVehicleName = v);
        Toggle("Status de Trava",        () => s.EspVehicleLock,     v => s.EspVehicleLock = v);
        Toggle("Distância",              () => s.EspVehicleDistance, v => s.EspVehicleDistance = v);

        var d = (int)s.VehicleMaxDistance; if (ModernSliderInt("Distância Máxima##v", ref d, 25, 1000, "{0} m")) s.VehicleMaxDistance = d;

        ImGui.Spacing();
        SectionTitle("CORES DE ESTADO");
        EditColor("Destrancado", s.VehicleUnlockedColor, v => s.VehicleUnlockedColor = v);
        EditColor("Trancado",    s.VehicleLockedColor,   v => s.VehicleLockedColor   = v);
        EditColor("Ocupado",     s.VehicleInsideColor,   v => s.VehicleInsideColor   = v);

        ImGui.Spacing();
        SectionTitle("AÇÕES RÁPIDAS (MAIS PRÓXIMO)");
        if (ActionButton("DESTRANCAR MAIS PRÓXIMO", "unlock", s.VehicleUnlockKey)) cheat.TryUnlockNearestVehicle();
        if (ActionButton("REPARAR MAIS PRÓXIMO", "fix", s.VehicleFixKey)) cheat.TryFixNearestVehicle();
        ImGui.EndChild();

        ImGui.SameLine(0, 12f);
        ImGui.BeginChild("vehicle_right", new Vector2(colW, avail.Y), ImGuiChildFlags.Borders);
        var vehicles = cheat.Vehicles;
        var local = cheat.LocalPosition;
        SectionTitle($"VEÍCULOS NO RANGE ({vehicles.Length})");

        ImGui.BeginChild("veh_sublist", new Vector2(-1, 190), ImGuiChildFlags.Borders);
        foreach (var v in vehicles)
        {
            var name  = string.IsNullOrWhiteSpace(v.Name) ? "Vehicle" : v.Name;
            var label = $"{name} | {Vector3.Distance(local, v.Position):0}m | {VehicleLockText(v.LockState)}##veh_{v.Address}";
            if (ImGui.Selectable(label, selectedVehicle == v.Address)) selectedVehicle = v.Address;
        }
        ImGui.EndChild();

        var current = vehicles.FirstOrDefault(v => v.Address == selectedVehicle);
        if (current.Address == 0)
        {
            ImGui.TextDisabled("Selecione um veículo na lista acima para interagir.");
        }
        else
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), string.IsNullOrWhiteSpace(current.Name) ? "Veículo Selecionado" : current.Name);
            ImGui.Text($"Distância: {Vector3.Distance(local, current.Position):0} m | Trava: {VehicleLockText(current.LockState)}");
            ImGui.Spacing();

            if (ImGui.Button("ENTRAR COMO MOTORISTA", new Vector2(-1, 24)))
                vehicleActionStatus = cheat.TryEnterVehicle(current.Address) ? "Entrada remota solicitada." : "Falha ao entrar.";
            if (ImGui.Button("PUXAR VEÍCULO ATÉ VOCÊ", new Vector2(-1, 24)))
                vehicleActionStatus = cheat.TryPullVehicleToPlayer(current.Address) ? "Veículo teletransportado." : "Falha ao puxar.";
            if (ImGui.Button("DESTRANCAR ESTE VEÍCULO", new Vector2(-1, 24)))
                vehicleActionStatus = cheat.TryUnlockVehicle(current.Address) ? "Veículo destrancado." : "Falha ao destrancar.";
            if (ImGui.Button("REPARAR ESTE VEÍCULO", new Vector2(-1, 24)))
                vehicleActionStatus = cheat.TryFixVehicle(current.Address) ? "Veículo reparado." : "Falha ao reparar.";

            if (!string.IsNullOrEmpty(vehicleActionStatus))
                ImGui.TextColored(new Vector4(.2f, 1f, .4f, 1f), vehicleActionStatus);
        }
        ImGui.EndChild();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Tab: PLAYERS (Lista e Detalhes)
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawPlayerListMenu()
    {
        var avail = ImGui.GetContentRegionAvail();
        var colW = (avail.X - 12f) * 0.5f;
        var players = cheat.Entities.Where(e => e.IsPlayer).ToArray();
        var local = cheat.LocalPosition;

        ImGui.BeginChild("players_list_col", new Vector2(colW, avail.Y), ImGuiChildFlags.Borders);
        SectionTitle($"JOGADORES DETECTADOS ({players.Length})");

        foreach (var p in players)
        {
            var dist  = Vector3.Distance(local, p.Position);
            var label = $"{p.Name}  |  {dist:0}m##player_{p.Address}";
            if (ImGui.Selectable(label, selectedPlayer == p.Address)) selectedPlayer = p.Address;
        }
        ImGui.EndChild();

        ImGui.SameLine(0, 12f);
        ImGui.BeginChild("player_details_col", new Vector2(colW, avail.Y), ImGuiChildFlags.Borders);
        SectionTitle("CARTÃO DO JOGADOR");

        var player = players.FirstOrDefault(p => p.Address == selectedPlayer);
        if (player is null)
        {
            ImGui.TextDisabled("Selecione um jogador na coluna ao lado.");
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), player.Name);
            ImGui.Text($"Endereço: 0x{player.Address:X}");
            ImGui.Text($"Distância: {Vector3.Distance(local, player.Position):0.0} m");
            ImGui.Spacing();

            var hpRatio = player.MaxHealth > 0 ? Math.Clamp(player.Health / player.MaxHealth, 0f, 1f) : 0f;
            ImGui.Text($"Vida: {player.Health:0} / {player.MaxHealth:0}");
            ImGui.ProgressBar(hpRatio, new Vector2(-1, 14), $"{hpRatio * 100:0}%");

            ImGui.Spacing();
            var armorRatio = Math.Clamp(player.Armor / 100f, 0f, 1f);
            ImGui.Text($"Colete: {player.Armor:0} / 100");
            ImGui.ProgressBar(armorRatio, new Vector2(-1, 14), $"{player.Armor:0}");

            ImGui.Spacing();
            SectionTitle("POSIÇÃO NO MUNDO");
            ImGui.TextDisabled($"X: {player.Position.X:F2}  Y: {player.Position.Y:F2}  Z: {player.Position.Z:F2}");
        }
        ImGui.EndChild();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Tab: CONFIG / MISC (Crosshair, GodMode, Persistence)
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawConfigMiscMenu()
    {
        var s = cheat.Settings;
        var avail = ImGui.GetContentRegionAvail();
        var colW = (avail.X - 12f) * 0.5f;

        ImGui.BeginChild("config_left", new Vector2(colW, avail.Y), ImGuiChildFlags.Borders);
        SectionTitle("CUSTOMIZADOR DE MIRA (CROSSHAIR)");
        Toggle("Exibir Crosshair no Overlay", () => s.ShowCrosshair, v => s.ShowCrosshair = v);

        var cs = s.CrosshairStyle; var csOpts = new[] { "Plus (+)", "Ponto (Dot)", "Círculo (Circle)", "Cross + Ponto" };
        if (ImGui.Combo("Estilo##ch", ref cs, csOpts, csOpts.Length)) s.CrosshairStyle = cs;

        var csz = s.CrosshairSize;      if (ModernSliderFloat("Tamanho da Mira", ref csz, 4f, 40f, "{0:0} px")) s.CrosshairSize = csz;
        var cgt = s.CrosshairGap;       if (ModernSliderFloat("Abertura (Gap)", ref cgt, 0f, 20f, "{0:0.1} px")) s.CrosshairGap = cgt;
        var cth = s.CrosshairThickness; if (ModernSliderFloat("Espessura do Traço", ref cth, .5f, 4f, "{0:0.1} px")) s.CrosshairThickness = cth;
        EditColor("Cor da Mira", s.CrosshairColor, v => s.CrosshairColor = v);

        ImGui.Spacing();
        SectionTitle("MODOS ESPECIAIS & HUD");
        Toggle("God Mode (Invencibilidade)", () => s.GodMode, v => s.GodMode = v);
        Toggle("Stream Mode (Ocultar em gravações)", () => s.StreamMode, v => s.StreamMode = v);
        Toggle("Exibir Watermark / Status no Jogo", () => s.ShowStatusBar, v => s.ShowStatusBar = v);
        if (s.ShowStatusBar)
        {
            var pos = s.StatusBarPosition;
            var posOpts = new[] { "Superior Esquerdo", "Inferior Esquerdo", "Inferior Direito", "Superior Direito" };
            if (ImGui.Combo("Posição do HUD##hudpos", ref pos, posOpts, posOpts.Length)) s.StatusBarPosition = pos;
        }
        ImGui.EndChild();

        ImGui.SameLine(0, 12f);
        ImGui.BeginChild("config_right", new Vector2(colW, avail.Y), ImGuiChildFlags.Borders);
        SectionTitle("CONFIGURAÇÃO & PERSISTÊNCIA");
        if (ImGui.Button("SALVAR CONFIGURAÇÃO AGORA", new Vector2(-1, 30)))
            s.Save();

        ImGui.Spacing();
        SectionTitle("TECLAS DE ATALHO RÁPIDAS");
        ImGui.BulletText("INSERT: Abrir / Fechar Menu");
        ImGui.BulletText($"Aim Assist: [{(Forms.Keys)s.AimKey}]");
        ImGui.BulletText($"Silent Aim: [{(Forms.Keys)s.SilentKey}]");
        ImGui.BulletText($"Triggerbot: [{(Forms.Keys)s.TriggerKey}]");
        ImGui.EndChild();
    }



    // ──────────────────────────────────────────────────────────────────────────
    //  ESP rendering (entities)
    // ──────────────────────────────────────────────────────────────────────────
    private void RenderEntity(ImDrawListPtr draw, Matrix4x4 view, Entity entity, Vector3 local)
    {
        var elapsed = Math.Clamp((Environment.TickCount64 - entity.UpdatedAt) / 1000f, 0f, .08f);
        var position = entity.Position + entity.Velocity * elapsed;
        var matrix = entity.Matrix;
        var translation = position - entity.Position;
        matrix.M41 += translation.X; matrix.M42 += translation.Y; matrix.M43 += translation.Z;
        var distance = Vector3.Distance(position, local);

        if (distance > cheat.Settings.EspMaxDistance ||
            !Project(view, position, out var basePoint)      ||
            !Project(view, entity.Bone(0, matrix), out var head) ||
            !Project(view, entity.Bone(7, matrix), out var neck) ||
            !Project(view, entity.Bone(1, matrix), out var leftFoot) ||
            !Project(view, entity.Bone(2, matrix), out var rightFoot))
        { GetVisibility(entity.Address, false); return; }

        var top    = head.Y - (neck.Y - head.Y) * 2.5f;
        var bottom = Math.Max(leftFoot.Y, rightFoot.Y) * 1.001f;
        var height = bottom - top;
        if (height is < 5 or > 3000) { GetVisibility(entity.Address, false); return; }

        var opacity   = GetVisibility(entity.Address, true);
        if (opacity < .02f) return;
        var halfWidth = height / 3.5f;
        var min = new Vector2(basePoint.X - halfWidth, top);
        var max = new Vector2(basePoint.X + halfWidth, bottom);
        var color = WithOpacity(ImGui.ColorConvertFloat4ToU32(entity.IsPlayer ? cheat.Settings.PlayerColor : cheat.Settings.NpcColor), opacity);
        var white = WithOpacity(0xFFFFFFFF, opacity);
        var black = WithOpacity(0xB4000000, opacity);

        if (cheat.Settings.EspBox)
        {
            if (cheat.Settings.EspReferenceStyle)
                DrawReferenceBox(draw, min, max, color, opacity, cheat.Settings.EspBoxThickness);
            else
            {
                draw.AddRect(min, max, black, 0, ImDrawFlags.None, cheat.Settings.EspBoxThickness + 1.6f);
                draw.AddRect(min, max, color, 0, ImDrawFlags.None, cheat.Settings.EspBoxThickness);
            }
        }

        if (cheat.Settings.EspHealth)
        {
            var health = entity.MaxHealth <= 0 ? 0 : Math.Clamp(entity.Health / entity.MaxHealth, 0, 1);
            if (cheat.Settings.EspReferenceStyle)
            {
                var x = min.X - 6f;
                draw.AddRectFilled(new Vector2(x, min.Y), new Vector2(x + 4f, max.Y), WithOpacity(0xC82D3027, opacity), 2f);
                if (health > 0) draw.AddRectFilled(new Vector2(x, max.Y - height * health), new Vector2(x + 4f, max.Y), WithOpacity(ImGui.ColorConvertFloat4ToU32(cheat.Settings.ReferenceHealthColor), opacity), 2f);
            }
            else
            {
                var x = min.X - 4;
                draw.AddLine(new Vector2(x, max.Y), new Vector2(x, min.Y), black, 3f);
                draw.AddLine(new Vector2(x, max.Y), new Vector2(x, max.Y - height * health), WithOpacity(0xFF33E05B, opacity), 1.5f);
            }
        }

        if (cheat.Settings.EspSkeleton)
        {
            RenderSkeleton(draw, view, entity, matrix, min, max, WithOpacity(ImGui.ColorConvertFloat4ToU32(cheat.Settings.SkeletonColor), opacity), cheat.Settings.EspSkeletonThickness);
            if (cheat.Settings.EspReferenceStyle && cheat.Settings.EspHeadCross)
            {
                var radius = Math.Clamp(MathF.Abs(neck.Y - head.Y) * .65f, 4f, 16f);
                var red    = WithOpacity(0xF0EB2D2D, opacity);
                draw.AddLine(head - new Vector2(radius), head + new Vector2(radius), red, 1.5f);
                draw.AddLine(new Vector2(head.X + radius, head.Y - radius), new Vector2(head.X - radius, head.Y + radius), red, 1.5f);
            }
        }

        if (cheat.Settings.EspName || cheat.Settings.EspDistance || cheat.Settings.EspWeapon)
        {
            var label = cheat.Settings.EspName ? entity.Name : string.Empty;
            if (cheat.Settings.EspDistance) label = $"{label}{(string.IsNullOrEmpty(label) ? string.Empty : " - ")}{distance:0.0}m";
            var weaponLabel = cheat.Settings.EspWeapon ? entity.WeaponName : string.Empty;
            if (cheat.Settings.EspReferenceStyle)
                DrawReferenceLabel(draw, basePoint.X, max.Y + 4f, label, weaponLabel, opacity, cheat.Settings.EspTextSize);
            else
            {
                var size      = ImGui.CalcTextSize(label) * (cheat.Settings.EspTextSize / 16f);
                var textPoint = new Vector2(basePoint.X - size.X / 2f, top - cheat.Settings.EspTextSize - 3);
                DrawEspText(draw, textPoint + Vector2.One, WithOpacity(0xC0000000, opacity), label, cheat.Settings.EspTextSize);
                DrawEspText(draw, textPoint,               white, label, cheat.Settings.EspTextSize);
                if (!string.IsNullOrEmpty(weaponLabel))
                {
                    var weaponSize = ImGui.CalcTextSize(weaponLabel) * (cheat.Settings.EspTextSize / 16f);
                    var weaponPoint = new Vector2(basePoint.X - weaponSize.X / 2f, max.Y + 3f);
                    DrawEspText(draw, weaponPoint + Vector2.One, WithOpacity(0xC0000000, opacity), weaponLabel, cheat.Settings.EspTextSize);
                    DrawEspText(draw, weaponPoint,               white, weaponLabel, cheat.Settings.EspTextSize);
                }
            }
        }
    }

    private static void DrawReferenceBox(ImDrawListPtr draw, Vector2 min, Vector2 max, uint color, float opacity, float thickness)
    {
        var outline = WithOpacity(0xB4000000, opacity);
        var corner  = (max.X - min.X) * .30f;
        void Line(Vector2 from, Vector2 to) { draw.AddLine(from, to, outline, thickness + 1.6f); draw.AddLine(from, to, color, thickness); }
        Line(min,                           new Vector2(min.X, max.Y));
        Line(new Vector2(max.X, min.Y),     max);
        Line(min,                           new Vector2(min.X + corner, min.Y));
        Line(new Vector2(max.X - corner, min.Y), new Vector2(max.X, min.Y));
    }

    private void DrawReferenceLabel(ImDrawListPtr draw, float centerX, float y, string label, string weaponLabel, float opacity, float textSize)
    {
        var hasMain = !string.IsNullOrEmpty(label);
        var hasWeapon = !string.IsNullOrEmpty(weaponLabel);
        if (!hasMain && !hasWeapon) return;

        // Same bottom-label treatment as the native VZX ESP: clean text
        // directly below the box, without a panel obscuring the game world.
        if (espFont.NativePtr != null) ImGui.PushFont(espFont);
        try
        {
            var textY = y;
            if (hasMain)
            {
                DrawOutlinedCenteredEspText(draw, centerX, textY, label, WithOpacity(0xFFF0F0F0, opacity), textSize);
                // The second line sits immediately beneath the first, matching
                // the compact native ESP label rather than leaving a card gap.
                textY += ImGui.CalcTextSize(label).Y * (textSize / 16f);
            }
            if (hasWeapon)
                DrawOutlinedCenteredEspText(draw, centerX, textY, weaponLabel, WithOpacity(0xFFCDD2DF, opacity), textSize);
        }
        finally { if (espFont.NativePtr != null) ImGui.PopFont(); }
    }

    private void DrawOutlinedCenteredEspText(ImDrawListPtr draw, float centerX, float y, string text, uint color, float size)
    {
        var textSize = ImGui.CalcTextSize(text) * (size / 16f);
        var origin = new Vector2(centerX - textSize.X * .5f, y);
        const uint outline = 0xE0000000;
        DrawEspText(draw, origin + new Vector2(-1, 0), outline, text, size);
        DrawEspText(draw, origin + new Vector2( 1, 0), outline, text, size);
        DrawEspText(draw, origin + new Vector2(0,-1), outline, text, size);
        DrawEspText(draw, origin + new Vector2(0, 1), outline, text, size);
        DrawEspText(draw, origin, color, text, size);
    }

    private float GetVisibility(nint address, bool visible)
    {
        var now = Environment.TickCount64;
        if (!entityFade.TryGetValue(address, out var fade))
        { fade = new FadeMotion { LastFrameAt = now }; entityFade[address] = fade; }
        var seconds = Math.Clamp((now - fade.LastFrameAt) / 1000f, 0f, .1f);
        fade.Opacity += ((visible ? 1f : 0f) - fade.Opacity) * (1f - MathF.Exp(-seconds / .12f));
        fade.LastFrameAt = now;
        return fade.Opacity;
    }

    private static uint WithOpacity(uint color, float opacity)
    {
        var alpha = (uint)Math.Clamp((int)((color >> 24) * opacity), 0, 255);
        return (color & 0x00FFFFFF) | (alpha << 24);
    }

    private void RenderSkeleton(ImDrawListPtr draw, Matrix4x4 view, Entity entity, Matrix4x4 matrix, Vector2 min, Vector2 max, uint color, float thickness)
    {
        var bones = new Dictionary<int, Vector3>
        {
            [31086]=entity.ComplexBone(31086), [39317]=entity.ComplexBone(39317),
            [11816]=entity.ComplexBone(11816), [23553]=entity.ComplexBone(23553),
            [64729]=entity.ComplexBone(64729), [45509]=entity.ComplexBone(45509),
            [0x58B7]=entity.ComplexBone(0x58B7), [61163]=entity.ComplexBone(61163),
            [18905]=entity.ComplexBone(18905), [10706]=entity.ComplexBone(10706),
            [40269]=entity.ComplexBone(40269), [0xBB0]=entity.ComplexBone(0xBB0),
            [28252]=entity.ComplexBone(28252), [57005]=entity.ComplexBone(57005),
            [58271]=entity.ComplexBone(58271), [63931]=entity.ComplexBone(63931),
            [14201]=entity.ComplexBone(14201), [2108]=entity.ComplexBone(2108),
            [51826]=entity.ComplexBone(51826), [36864]=entity.ComplexBone(36864),
            [52301]=entity.ComplexBone(52301), [20781]=entity.ComplexBone(20781)
        };
        if (bones[39317] == Vector3.Zero || bones[31086] == Vector3.Zero) return;
        int[,] segments = { {31086,39317},{39317,64729},{64729,45509},{45509,0x58B7},{0x58B7,61163},{61163,18905},{39317,10706},{10706,40269},{40269,0xBB0},{0xBB0,28252},{28252,57005},{39317,23553},{23553,11816},{11816,58271},{58271,63931},{63931,14201},{14201,2108},{11816,51826},{51826,36864},{36864,52301},{52301,20781} };
        var body = new Vector4(min.X-(max.X-min.X)*.25f, min.Y-(max.Y-min.Y)*.08f, max.X+(max.X-min.X)*.25f, max.Y+(max.Y-min.Y)*.08f);
        for (var i = 0; i < segments.GetLength(0); i++)
            if (Project(view, bones[segments[i,0]], out var a) && Project(view, bones[segments[i,1]], out var b) && Inside(body,a) && Inside(body,b))
                draw.AddLine(a, b, color, thickness);
    }

    private void RenderVehicle(ImDrawListPtr draw, Matrix4x4 view, Vehicle vehicle, Vector3 local)
    {
        var elapsed  = Math.Clamp((Environment.TickCount64 - vehicle.UpdatedAt) / 1000f, 0f, .08f);
        var position = vehicle.Position + vehicle.Velocity * elapsed;
        var distance = Vector3.Distance(position, local);
        if (distance > cheat.Settings.VehicleMaxDistance || !Project(view, position, out var point)) return;
        if (espFont.NativePtr != null) ImGui.PushFont(espFont);
        try
        {
            var offsetY = -14f;
            if (cheat.Settings.EspVehicleLock) { var (status, col) = VehicleLockLabel(vehicle.LockState); DrawOutlinedVehicleText(draw, status, new Vector2(point.X, point.Y + offsetY), col, cheat.Settings.EspTextSize * .95f); offsetY += 15f; }
            if (cheat.Settings.EspVehicleName) { var name = string.IsNullOrWhiteSpace(vehicle.Name) ? "Vehicle" : vehicle.Name; DrawOutlinedVehicleText(draw, name, new Vector2(point.X, point.Y + offsetY), ImGui.ColorConvertFloat4ToU32(cheat.Settings.VehicleNameColor), cheat.Settings.EspTextSize); offsetY += 15f; }
            if (cheat.Settings.EspVehicleDistance) DrawOutlinedVehicleText(draw, $"{distance:0}m", new Vector2(point.X, point.Y + offsetY), ImGui.ColorConvertFloat4ToU32(cheat.Settings.VehicleDistanceColor), cheat.Settings.EspTextSize * .88f);
        }
        finally { if (espFont.NativePtr != null) ImGui.PopFont(); }
    }

    private (string Label, uint Color) VehicleLockLabel(uint lockState) => lockState switch
    {
        0 or 1 => ("[Unlocked]", ImGui.ColorConvertFloat4ToU32(cheat.Settings.VehicleUnlockedColor)),
        2      => ("[Locked]",   ImGui.ColorConvertFloat4ToU32(cheat.Settings.VehicleLockedColor)),
        3      => ("[LockedForPlayer]", ImGui.ColorConvertFloat4ToU32(cheat.Settings.VehicleLockedColor)),
        4      => ("[Inside]",   ImGui.ColorConvertFloat4ToU32(cheat.Settings.VehicleInsideColor)),
        _ when lockState > 1 => ("[Locked]", ImGui.ColorConvertFloat4ToU32(cheat.Settings.VehicleLockedColor)),
        _ => ("[Unlocked]", ImGui.ColorConvertFloat4ToU32(cheat.Settings.VehicleUnlockedColor))
    };

    private static string VehicleLockText(uint lockState) => lockState switch
    {
        1 => "Destrancado", 2 or 3 or 4 => "Trancado", 10 => "Dentro", _ => "Desconhecido"
    };

    // ──────────────────────────────────────────────────────────────────────────
    //  Drawing helpers
    // ──────────────────────────────────────────────────────────────────────────
    private void DrawOutlinedVehicleText(ImDrawListPtr draw, string text, Vector2 center, uint color, float size)
    {
        var textSize = ImGui.CalcTextSize(text) * (size / 16f);
        var origin   = new Vector2(center.X - textSize.X / 2f, center.Y);
        const uint outline = 0xE0000000;
        DrawEspText(draw, origin + new Vector2(-1, 0), outline, text, size);
        DrawEspText(draw, origin + new Vector2( 1, 0), outline, text, size);
        DrawEspText(draw, origin + new Vector2(0,-1),  outline, text, size);
        DrawEspText(draw, origin + new Vector2(0, 1),  outline, text, size);
        DrawEspText(draw, origin,                       color,   text, size);
    }

    private void DrawEspText(ImDrawListPtr draw, Vector2 pos, uint color, string text, float size)
    {
        if (espFont.NativePtr != null) draw.AddText(espFont, size, pos, color, text);
        else draw.AddText(pos, color, text);
    }



    private bool Project(Matrix4x4 view, Vector3 world, out Vector2 point)
    {
        if (cheat.WorldToScreen(view, world, viewport, out var proj)) { point = new Vector2(proj.X, proj.Y); return true; }
        point = default; return false;
    }

    private static bool Inside(Vector4 area, Vector2 p) =>
        p.X >= area.X && p.X <= area.Z && p.Y >= area.Y && p.Y <= area.W;
}
