using System.Numerics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace FiveMExternal;

public sealed class OverlayForm : Form
{
    private readonly CheatController cheat;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    private readonly Panel menu = new();
    private bool menuKeyWasDown;

    public OverlayForm(CheatController cheat)
    {
        this.cheat = cheat;
        Text = "DDL";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
        CreateMenu();
        timer.Tick += (_, _) => TickOverlay();
        timer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_NCHITTEST && !cheat.Settings.ShowMenu) { m.Result = (IntPtr)NativeMethods.HTTRANSPARENT; return; }
        base.WndProc(ref m);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyInputMode();
    }

    private void CreateMenu()
    {
        menu.Size = new Size(300, 255);
        menu.Location = new Point(32, 32);
        menu.BackColor = Color.FromArgb(235, 12, 20, 33);
        menu.Visible = cheat.Settings.ShowMenu;
        var title = new Label { Text = "VZX FiveM  |  C#", ForeColor = Color.White, Font = new Font("Segoe UI", 13, FontStyle.Bold), Location = new Point(16, 14), AutoSize = true };
        menu.Controls.Add(title);
        AddToggle("ESP", 55, value => cheat.Settings.EspEnabled = value, cheat.Settings.EspEnabled);
        AddToggle("Box ESP", 84, value => cheat.Settings.EspBox = value, cheat.Settings.EspBox);
        AddToggle("Nome e distância", 113, value => { cheat.Settings.EspName = value; cheat.Settings.EspDistance = value; }, true);
        AddToggle("Skeleton", 142, value => cheat.Settings.EspSkeleton = value, cheat.Settings.EspSkeleton);
        AddToggle("Veículos", 171, value => cheat.Settings.EspVehicles = value, cheat.Settings.EspVehicles);
        AddToggle("God mode", 200, value => cheat.Settings.GodMode = value, cheat.Settings.GodMode);
        Controls.Add(menu);
    }

    private void AddToggle(string label, int top, Action<bool> changed, bool value)
    {
        var box = new CheckBox { Text = label, Checked = value, ForeColor = Color.Gainsboro, BackColor = Color.Transparent, AutoSize = true, Location = new Point(18, top) };
        box.CheckedChanged += (_, _) => changed(box.Checked);
        menu.Controls.Add(box);
    }

    private void TickOverlay()
    {
        var isMenuKeyDown = NativeMethods.IsKeyDown(cheat.Settings.MenuKey);
        if (isMenuKeyDown && !menuKeyWasDown)
        {
            cheat.Settings.ShowMenu = !cheat.Settings.ShowMenu;
            menu.Visible = cheat.Settings.ShowMenu;
            ApplyInputMode();
        }
        menuKeyWasDown = isMenuKeyDown;
        SyncToGame();
        Invalidate();
    }

    private void ApplyInputMode()
    {
        var style = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
        style = cheat.Settings.ShowMenu ? style & ~NativeMethods.WS_EX_TRANSPARENT : style | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE, style);
    }

    private void SyncToGame()
    {
        var target = GetTargetRect();
        if (target is null) { Hide(); return; }
        if (!Visible) Show();
        // Avoid a redundant native resize/recomposition on every paint tick.
        if (Bounds.Location != target.Value.Location || Bounds.Size != target.Value.Size)
            Bounds = target.Value;
    }

    private Rectangle? GetTargetRect()
    {
        // grcWindow is the same FiveM target class used by the C++ project.
        var hwnd = NativeMethods.FindWindow("grcWindow", null);
        if (hwnd == IntPtr.Zero || !NativeMethods.GetClientRect(hwnd, out var rect)) return null;
        var origin = new NativeMethods.POINT();
        if (!NativeMethods.ClientToScreen(hwnd, ref origin)) return null;
        return new Rectangle(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        // ClearType blends against the magenta chroma-key background and produces
        // pink/green fringes. Grayscale antialiasing preserves a clean white outline.
        e.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        base.OnPaint(e);
        if (cheat.Settings.Crosshair) DrawCrosshair(e.Graphics);
        if (!cheat.Settings.EspEnabled) return;
        if (cheat.TryGetViewMatrix(out var view))
        {
            var localPosition = cheat.LocalPosition;
            foreach (var entity in cheat.Entities) DrawEntity(e.Graphics, view, entity, localPosition);
            if (cheat.Settings.EspVehicles) foreach (var vehicle in cheat.Vehicles) DrawVehicle(e.Graphics, view, vehicle, localPosition);
        }
        DrawStatus(e.Graphics);
    }

    private void DrawCrosshair(Graphics g)
    {
        using var pen = new Pen(Color.White, 1.5f);
        var c = new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        g.DrawLine(pen, c.X - 7, c.Y, c.X + 7, c.Y);
        g.DrawLine(pen, c.X, c.Y - 7, c.X, c.Y + 7);
    }

    private void DrawStatus(Graphics g)
    {
        using var brush = new SolidBrush(Color.FromArgb(220, 220, 230, 240));
        g.DrawString($"Entities: {cheat.Entities.Length}  Vehicles: {cheat.Vehicles.Length}", Font, brush, 12, ClientSize.Height - 28);
    }

    private void DrawEntity(Graphics g, Matrix4x4 view, Entity entity, Vector3 localPosition)
    {
        var distance = Vector3.Distance(entity.Position, localPosition);
        if (distance > cheat.Settings.EspMaxDistance ||
            !cheat.WorldToScreen(view, entity.Position, ClientSize, out var basePoint) ||
            !cheat.WorldToScreen(view, entity.Bone(0), ClientSize, out var headPoint) ||
            !cheat.WorldToScreen(view, entity.Bone(7), ClientSize, out var neckPoint) ||
            !cheat.WorldToScreen(view, entity.Bone(1), ClientSize, out var leftFoot) ||
            !cheat.WorldToScreen(view, entity.Bone(2), ClientSize, out var rightFoot)) return;

        var top = headPoint.Y - (neckPoint.Y - headPoint.Y) * 2.5f;
        var bottom = Math.Max(leftFoot.Y, rightFoot.Y) * 1.001f;
        var height = bottom - top;
        if (height is < 4 or > 3000) return;
        var halfWidth = height / 3.5f;
        var bounds = new RectangleF(basePoint.X - halfWidth, top, halfWidth * 2, height);
        var color = Color.White;
        using var pen = new Pen(color, 1.25f) { LineJoin = LineJoin.Round };
        using var outline = new Pen(Color.FromArgb(190, 0, 0, 0), 3.0f) { LineJoin = LineJoin.Round };
        if (cheat.Settings.EspBox) { g.DrawRectangle(outline, bounds.X, bounds.Y, bounds.Width, bounds.Height); g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height); }
        if (cheat.Settings.EspHealth)
        {
            var health = entity.MaxHealth <= 0 ? 0 : Math.Clamp(entity.Health / entity.MaxHealth, 0, 1);
            using var healthPen = new Pen(Color.LimeGreen, 2);
            g.DrawLine(healthPen, bounds.Left - 4, bounds.Bottom, bounds.Left - 4, bounds.Bottom - bounds.Height * health);
        }
        if (cheat.Settings.EspSkeleton)
        {
            // Full crSkeletonData is expensive through managed ReadProcessMemory.
            // Keep it on nearby players; distant entities retain the clean 9-bone fallback.
            if (distance <= 150f) DrawComplexSkeleton(g, view, entity, pen, bounds);
            else DrawSimpleSkeleton(g, view, entity, pen);
        }
        if (cheat.Settings.EspName || cheat.Settings.EspDistance)
        {
            var text = cheat.Settings.EspName ? entity.Name : string.Empty;
            if (cheat.Settings.EspDistance) text = $"{text} {distance:0}m".Trim();
            using var brush = new SolidBrush(color);
            g.DrawString(text, Font, brush, bounds.Left, bounds.Top - 18);
        }
    }

    private void DrawComplexSkeleton(Graphics g, Matrix4x4 view, Entity entity, Pen pen, RectangleF bodyBounds)
    {
        var bones = new Dictionary<int, Vector3> {
            [31086] = entity.ComplexBone(31086), [39317] = entity.ComplexBone(39317), [11816] = entity.ComplexBone(11816), [23553] = entity.ComplexBone(23553),
            [64729] = entity.ComplexBone(64729), [45509] = entity.ComplexBone(45509), [0x58B7] = entity.ComplexBone(0x58B7), [61163] = entity.ComplexBone(61163), [18905] = entity.ComplexBone(18905),
            [10706] = entity.ComplexBone(10706), [40269] = entity.ComplexBone(40269), [0xBB0] = entity.ComplexBone(0xBB0), [28252] = entity.ComplexBone(28252), [57005] = entity.ComplexBone(57005),
            [58271] = entity.ComplexBone(58271), [63931] = entity.ComplexBone(63931), [14201] = entity.ComplexBone(14201), [2108] = entity.ComplexBone(2108),
            [51826] = entity.ComplexBone(51826), [36864] = entity.ComplexBone(36864), [52301] = entity.ComplexBone(52301), [20781] = entity.ComplexBone(20781) };
        if (bones[39317] == Vector3.Zero || bones[31086] == Vector3.Zero) { DrawSimpleSkeleton(g, view, entity, pen); return; }
        int[,] segments = { { 31086, 39317 }, { 39317, 64729 }, { 64729, 45509 }, { 45509, 0x58B7 }, { 0x58B7, 61163 }, { 61163, 18905 }, { 39317, 10706 }, { 10706, 40269 }, { 40269, 0xBB0 }, { 0xBB0, 28252 }, { 28252, 57005 }, { 39317, 23553 }, { 23553, 11816 }, { 11816, 58271 }, { 58271, 63931 }, { 63931, 14201 }, { 14201, 2108 }, { 11816, 51826 }, { 51826, 36864 }, { 36864, 52301 }, { 52301, 20781 } };
        var validArea = bodyBounds;
        validArea.Inflate(bodyBounds.Width * .70f, bodyBounds.Height * .15f);
        var drawn = 0;
        for (var i = 0; i < segments.GetLength(0); i++)
        {
            if (!cheat.WorldToScreen(view, bones[segments[i, 0]], ClientSize, out var first) || !cheat.WorldToScreen(view, bones[segments[i, 1]], ClientSize, out var second)) continue;
            if (!validArea.Contains(first) || !validArea.Contains(second)) continue;
            g.DrawLine(pen, first, second);
            drawn++;
        }
        if (drawn == 0) DrawSimpleSkeleton(g, view, entity, pen);
    }

    private void DrawSimpleSkeleton(Graphics g, Matrix4x4 view, Entity entity, Pen pen)
    {
        DrawBone(g, view, entity.Bone(0), entity.Bone(7), pen); DrawBone(g, view, entity.Bone(7), entity.Bone(6), pen); DrawBone(g, view, entity.Bone(7), entity.Bone(5), pen); DrawBone(g, view, entity.Bone(7), entity.Bone(8), pen); DrawBone(g, view, entity.Bone(8), entity.Bone(3), pen); DrawBone(g, view, entity.Bone(8), entity.Bone(4), pen); DrawBone(g, view, entity.Bone(3), entity.Bone(1), pen); DrawBone(g, view, entity.Bone(4), entity.Bone(2), pen);
    }

    private void DrawBone(Graphics g, Matrix4x4 view, PointF first, Vector3 secondWorld, Pen pen)
    {
        if (cheat.WorldToScreen(view, secondWorld, ClientSize, out var second)) g.DrawLine(pen, first, second);
    }

    private void DrawBone(Graphics g, Matrix4x4 view, Vector3 firstWorld, Vector3 secondWorld, Pen pen)
    {
        if (cheat.WorldToScreen(view, firstWorld, ClientSize, out var first) && cheat.WorldToScreen(view, secondWorld, ClientSize, out var second)) g.DrawLine(pen, first, second);
    }

    private void DrawVehicle(Graphics g, Matrix4x4 view, Vehicle vehicle, Vector3 localPosition)
    {
        var distance = Vector3.Distance(vehicle.Position, localPosition);
        if (distance > cheat.Settings.VehicleMaxDistance || !cheat.WorldToScreen(view, vehicle.Position, ClientSize, out var point)) return;

        var offsetY = -14f;
        if (cheat.Settings.EspVehicleLock)
        {
            var (status, color) = VehicleLockLabel(vehicle.LockState);
            DrawVehicleText(g, status, new PointF(point.X, point.Y + offsetY), color, 12.5f, FontStyle.Bold);
            offsetY += 15f;
        }
        if (cheat.Settings.EspVehicleName)
        {
            var name = string.IsNullOrWhiteSpace(vehicle.Name) ? "Vehicle" : vehicle.Name;
            DrawVehicleText(g, name, new PointF(point.X, point.Y + offsetY), ToColor(cheat.Settings.VehicleNameColor), 13f, FontStyle.Bold);
            offsetY += 15f;
        }
        if (cheat.Settings.EspVehicleDistance)
            DrawVehicleText(g, $"{distance:0}m", new PointF(point.X, point.Y + offsetY), ToColor(cheat.Settings.VehicleDistanceColor), 11.5f, FontStyle.Bold);
    }

    private (string Label, Color Color) VehicleLockLabel(uint lockState) => lockState switch
    {
        0 or 1 => ("[Unlocked]", ToColor(cheat.Settings.VehicleUnlockedColor)),
        2 => ("[Locked]", ToColor(cheat.Settings.VehicleLockedColor)),
        3 => ("[LockedForPlayer]", ToColor(cheat.Settings.VehicleLockedColor)),
        4 => ("[Inside]", ToColor(cheat.Settings.VehicleInsideColor)),
        _ when lockState > 1 => ("[Locked]", ToColor(cheat.Settings.VehicleLockedColor)),
        _ => ("[Unlocked]", ToColor(cheat.Settings.VehicleUnlockedColor))
    };

    private static Color ToColor(Vector4 value) => Color.FromArgb((int)(value.W * 255), (int)(value.X * 255), (int)(value.Y * 255), (int)(value.Z * 255));

    private void DrawVehicleText(Graphics g, string text, PointF point, Color color, float size, FontStyle style)
    {
        using var font = new Font(Font.FontFamily, size, style, GraphicsUnit.Pixel);
        using var outline = new SolidBrush(Color.FromArgb(220, 0, 0, 0));
        using var foreground = new SolidBrush(color);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
        foreach (var offset in new[] { new PointF(-1, 0), new PointF(1, 0), new PointF(0, -1), new PointF(0, 1) })
            g.DrawString(text, font, outline, point.X + offset.X, point.Y + offset.Y, format);
        g.DrawString(text, font, foreground, point, format);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
