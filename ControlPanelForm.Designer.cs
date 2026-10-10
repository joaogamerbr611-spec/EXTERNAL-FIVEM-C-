#nullable enable

using VzxWidgets.Controls;

namespace FiveMExternal;

public partial class ControlPanelForm
{
    private System.ComponentModel.IContainer? components;
    private Panel visualPage = null!, aimPage = null!, silentPage = null!;
    private VzxButton visualTab = null!, aimTab = null!, silentTab = null!;
    private VzxToggleSwitch espCheckBox = null!, boxCheckBox = null!, nameCheckBox = null!, skeletonCheckBox = null!, vehicleCheckBox = null!, godModeCheckBox = null!;
    private VzxTrackBar espDistanceUpDown = null!, vehicleDistanceUpDown = null!;
    private VzxColorButton playerColorButton = null!, npcColorButton = null!, skeletonColorButton = null!;
    private VzxToggleSwitch aimEnabledCheckBox = null!, aimFovCheckBox = null!, aimPlayersCheckBox = null!, aimPredictionCheckBox = null!, aimAlwaysActiveCheckBox = null!, aimClosestCheckBox = null!;
    private VzxTrackBar aimFovUpDown = null!, aimSmoothUpDown = null!, aimDistanceUpDown = null!;
    private VzxComboBox aimBoneComboBox = null!;
    private VzxKeybind aimKeyButton = null!;
    private VzxToggleSwitch silentEnabledCheckBox = null!, silentFovCheckBox = null!, silentPlayersCheckBox = null!, silentClosestCheckBox = null!;
    private VzxTrackBar silentFovUpDown = null!, silentDistanceUpDown = null!;
    private VzxComboBox silentBoneComboBox = null!;
    private VzxKeybind silentKeyButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();
        BackColor = Color.FromArgb(12, 16, 24); ForeColor = Color.FromArgb(229, 235, 245);
        Font = new Font("Segoe UI", 9F); ClientSize = new Size(1060, 690); MinimumSize = new Size(1060, 690);
        StartPosition = FormStartPosition.CenterScreen; Text = "VZX FiveM"; FormBorderStyle = FormBorderStyle.None;
        BorderRadius = 16; BorderSize = 1; BorderColor = Color.FromArgb(46, 57, 78); HasDropShadow = true;

        var sidebar = new Panel { BackColor = Color.FromArgb(17, 22, 32), Location = Point.Empty, Size = new Size(225, 690) };
        var logo = Label("VZX", new Point(28, 32), new Size(165, 35), 21, Color.FromArgb(40, 195, 255), FontStyle.Bold);
        var product = Label("FIVEM  /  EXTERNAL", new Point(30, 68), new Size(165, 20), 9, Color.FromArgb(137, 153, 177));
        var line = new Panel { BackColor = Color.FromArgb(47, 59, 80), Location = new Point(28, 106), Size = new Size(168, 1) };
        visualTab = TabButton("VISUAL", new Point(18, 139)); visualTab.Click += visualTab_Click;
        aimTab = TabButton("AIM ASSIST", new Point(18, 194)); aimTab.Click += aimTab_Click;
        silentTab = TabButton("SILENT AIM", new Point(18, 249)); silentTab.Click += silentTab_Click;
        sidebar.Controls.AddRange(new Control[] { logo, product, line, visualTab, aimTab, silentTab, Label("VZX WIDGETS  •  .NET 8", new Point(28, 643), new Size(170, 20), 8, Color.FromArgb(103, 120, 145)) });

        var heading = Label("CONTROL PANEL", new Point(263, 31), new Size(380, 33), 19, Color.White, FontStyle.Bold);
        var subheading = Label("Configuração em tempo real", new Point(265, 67), new Size(310, 23), 10, Color.FromArgb(142, 157, 181));
        var minimize = Button("—", new Point(950, 25), new Size(32, 30)); minimize.Click += minimizeButton_Click;
        var close = Button("×", new Point(990, 25), new Size(40, 30)); close.Font = new Font("Segoe UI", 14F); close.HoverColor = Color.FromArgb(181, 57, 80); close.Click += closeButton_Click;
        visualPage = Page(); aimPage = Page(); silentPage = Page();
        BuildVisualPage(); BuildAimPage(); BuildSilentPage();
        Controls.AddRange(new Control[] { sidebar, heading, subheading, minimize, close, visualPage, aimPage, silentPage });
        ResumeLayout(false);
    }

    private void BuildVisualPage()
    {
        var visual = Card(new Point(0, 0), new Size(370, 265)); visual.Controls.Add(Header("VISUALS", new Point(20, 18)));
        espCheckBox = Toggle("ESP master", new Point(20, 62)); espCheckBox.CheckedChanged += espCheckBox_CheckedChanged;
        boxCheckBox = Toggle("Box ESP", new Point(20, 102)); boxCheckBox.CheckedChanged += boxCheckBox_CheckedChanged;
        nameCheckBox = Toggle("Nome e distância", new Point(20, 142)); nameCheckBox.CheckedChanged += nameCheckBox_CheckedChanged;
        skeletonCheckBox = Toggle("Skeleton complexo", new Point(20, 182)); skeletonCheckBox.CheckedChanged += skeletonCheckBox_CheckedChanged;
        vehicleCheckBox = Toggle("Veículos", new Point(205, 62)); vehicleCheckBox.CheckedChanged += vehicleCheckBox_CheckedChanged;
        godModeCheckBox = Toggle("God mode", new Point(205, 102)); godModeCheckBox.CheckedChanged += godModeCheckBox_CheckedChanged;
        visual.Controls.AddRange(new Control[] { espCheckBox, boxCheckBox, nameCheckBox, skeletonCheckBox, vehicleCheckBox, godModeCheckBox });

        var distance = Card(new Point(0, 280), new Size(370, 190)); distance.Controls.Add(Header("DISTÂNCIA", new Point(20, 18)));
        distance.Controls.Add(Label("ESP", new Point(20, 63), new Size(90, 20), 9, Muted));
        espDistanceUpDown = Slider(new Point(20, 89), 25, 2000); espDistanceUpDown.ValueChanged += espDistanceUpDown_ValueChanged;
        distance.Controls.Add(Label("VEÍCULOS", new Point(20, 123), new Size(100, 20), 9, Muted));
        vehicleDistanceUpDown = Slider(new Point(20, 149), 25, 1000); vehicleDistanceUpDown.ValueChanged += vehicleDistanceUpDown_ValueChanged;
        distance.Controls.AddRange(new Control[] { espDistanceUpDown, vehicleDistanceUpDown });

        var colors = Card(new Point(390, 0), new Size(370, 265)); colors.Controls.Add(Header("PALETA ESP", new Point(20, 18)));
        colors.Controls.Add(Label("JOGADORES", new Point(20, 67), new Size(150, 20), 9, Muted)); playerColorButton = ColorButton(new Point(225, 55)); playerColorButton.ColorChanged += playerColorButton_ColorChanged;
        colors.Controls.Add(Label("NPCS / OUTROS", new Point(20, 125), new Size(150, 20), 9, Muted)); npcColorButton = ColorButton(new Point(225, 113)); npcColorButton.ColorChanged += npcColorButton_ColorChanged;
        colors.Controls.Add(Label("SKELETON", new Point(20, 183), new Size(150, 20), 9, Muted)); skeletonColorButton = ColorButton(new Point(225, 171)); skeletonColorButton.ColorChanged += skeletonColorButton_ColorChanged;
        colors.Controls.AddRange(new Control[] { playerColorButton, npcColorButton, skeletonColorButton });

        var status = Card(new Point(390, 280), new Size(370, 190)); status.Controls.Add(Header("STATUS", new Point(20, 18)));
        status.Controls.Add(Label("As alterações são aplicadas imediatamente", new Point(20, 65), new Size(310, 24), 10, Muted));
        var online = Label("OVERLAY ONLINE", new Point(20, 105), new Size(190, 32), 10, Color.FromArgb(89, 225, 166), FontStyle.Bold); online.BackColor = Color.FromArgb(21, 57, 51); online.TextAlign = ContentAlignment.MiddleCenter; status.Controls.Add(online);
        visualPage.Controls.AddRange(new Control[] { visual, distance, colors, status });
    }

    private void BuildAimPage()
    {
        var behavior = Card(new Point(0, 0), new Size(370, 265)); behavior.Controls.Add(Header("AIM ASSIST", new Point(20, 18)));
        aimEnabledCheckBox = Toggle("Ativar aim", new Point(20, 62)); aimEnabledCheckBox.CheckedChanged += aimEnabledCheckBox_CheckedChanged;
        aimFovCheckBox = Toggle("Exibir FOV", new Point(20, 102)); aimFovCheckBox.CheckedChanged += aimFovCheckBox_CheckedChanged;
        aimPlayersCheckBox = Toggle("Somente jogadores", new Point(20, 142)); aimPlayersCheckBox.CheckedChanged += aimPlayersCheckBox_CheckedChanged;
        aimPredictionCheckBox = Toggle("Predição", new Point(205, 62)); aimPredictionCheckBox.CheckedChanged += aimPredictionCheckBox_CheckedChanged;
        aimAlwaysActiveCheckBox = Toggle("Sempre ativo", new Point(205, 102)); aimAlwaysActiveCheckBox.CheckedChanged += aimAlwaysActiveCheckBox_CheckedChanged;
        aimClosestCheckBox = Toggle("Priorizar próximo", new Point(205, 142)); aimClosestCheckBox.CheckedChanged += aimClosestCheckBox_CheckedChanged;
        behavior.Controls.AddRange(new Control[] { aimEnabledCheckBox, aimFovCheckBox, aimPlayersCheckBox, aimPredictionCheckBox, aimAlwaysActiveCheckBox, aimClosestCheckBox });
        var tuning = Card(new Point(0, 280), new Size(370, 230)); tuning.Controls.Add(Header("AJUSTES", new Point(20, 18)));
        tuning.Controls.Add(Label("FOV", new Point(20, 60), new Size(100, 20), 9, Muted)); aimFovUpDown = Slider(new Point(20, 84), 20, 500); aimFovUpDown.ValueChanged += aimFovUpDown_ValueChanged;
        tuning.Controls.Add(Label("SUAVIZAÇÃO", new Point(20, 115), new Size(120, 20), 9, Muted)); aimSmoothUpDown = Slider(new Point(20, 139), 1, 50); aimSmoothUpDown.ValueChanged += aimSmoothUpDown_ValueChanged;
        tuning.Controls.Add(Label("DISTÂNCIA", new Point(20, 170), new Size(120, 20), 9, Muted)); aimDistanceUpDown = Slider(new Point(20, 194), 10, 1000); aimDistanceUpDown.ValueChanged += aimDistanceUpDown_ValueChanged;
        tuning.Controls.AddRange(new Control[] { aimFovUpDown, aimSmoothUpDown, aimDistanceUpDown });
        var target = TargetCard("ALVO", true); aimPage.Controls.AddRange(new Control[] { behavior, tuning, target, NoteCard("PRIORIDADE", "O alvo mais próximo da mira é escolhido primeiro.", "Use a suavização para movimentos mais naturais.") });
    }

    private void BuildSilentPage()
    {
        var behavior = Card(new Point(0, 0), new Size(370, 265)); behavior.Controls.Add(Header("SILENT AIM", new Point(20, 18)));
        silentEnabledCheckBox = Toggle("Ativar silent", new Point(20, 62)); silentEnabledCheckBox.CheckedChanged += silentEnabledCheckBox_CheckedChanged;
        silentFovCheckBox = Toggle("Exibir FOV", new Point(20, 102)); silentFovCheckBox.CheckedChanged += silentFovCheckBox_CheckedChanged;
        silentPlayersCheckBox = Toggle("Somente jogadores", new Point(20, 142)); silentPlayersCheckBox.CheckedChanged += silentPlayersCheckBox_CheckedChanged;
        silentClosestCheckBox = Toggle("Priorizar próximo", new Point(205, 62)); silentClosestCheckBox.CheckedChanged += silentClosestCheckBox_CheckedChanged;
        behavior.Controls.AddRange(new Control[] { silentEnabledCheckBox, silentFovCheckBox, silentPlayersCheckBox, silentClosestCheckBox });
        var tuning = Card(new Point(0, 280), new Size(370, 190)); tuning.Controls.Add(Header("AJUSTES", new Point(20, 18)));
        tuning.Controls.Add(Label("FOV", new Point(20, 63), new Size(100, 20), 9, Muted)); silentFovUpDown = Slider(new Point(20, 89), 20, 500); silentFovUpDown.ValueChanged += silentFovUpDown_ValueChanged;
        tuning.Controls.Add(Label("DISTÂNCIA", new Point(20, 123), new Size(120, 20), 9, Muted)); silentDistanceUpDown = Slider(new Point(20, 149), 10, 1000); silentDistanceUpDown.ValueChanged += silentDistanceUpDown_ValueChanged;
        tuning.Controls.AddRange(new Control[] { silentFovUpDown, silentDistanceUpDown });
        var target = TargetCard("ALVO", false); silentPage.Controls.AddRange(new Control[] { behavior, tuning, target, NoteCard("SILENT STATUS", "Configure FOV, distância e tecla.", "A seleção acompanha as suas preferências.") });
    }

    private VzxCard TargetCard(string header, bool aim)
    {
        var card = Card(new Point(390, 0), new Size(370, 265)); card.Controls.Add(Header(header, new Point(20, 18)));
        card.Controls.Add(Label("OSSO ALVO", new Point(20, 65), new Size(140, 20), 9, Muted));
        var combo = Combo(new Point(20, 90)); combo.Items.AddRange(aim ? new object[] { "Cabeça", "Pescoço", "Torso", "Mão esquerda", "Mão direita", "Pé esquerdo", "Pé direito" } : new object[] { "Cabeça", "Torso", "Pé direito", "Pé esquerdo", "Mão direita", "Mão esquerda", "Pescoço" });
        card.Controls.Add(Label("TECLA DE ATIVAÇÃO", new Point(20, 150), new Size(180, 20), 9, Muted)); var key = Keybind(new Point(20, 175));
        if (aim) { aimBoneComboBox = combo; aimBoneComboBox.SelectedIndexChanged += aimBoneComboBox_SelectedIndexChanged; aimKeyButton = key; aimKeyButton.KeyChanged += aimKeyButton_KeyChanged; }
        else { silentBoneComboBox = combo; silentBoneComboBox.SelectedIndexChanged += silentBoneComboBox_SelectedIndexChanged; silentKeyButton = key; silentKeyButton.KeyChanged += silentKeyButton_KeyChanged; }
        card.Controls.AddRange(new Control[] { combo, key }); return card;
    }

    private static VzxCard NoteCard(string title, string line1, string line2) { var card = Card(new Point(390, 280), new Size(370, 230)); card.Controls.Add(Header(title, new Point(20, 18))); card.Controls.Add(Label(line1, new Point(20, 67), new Size(315, 45), 10, Muted)); card.Controls.Add(Label(line2, new Point(20, 128), new Size(315, 42), 10, Color.FromArgb(142,157,181))); return card; }
    private static readonly Color Muted = Color.FromArgb(190, 202, 220);
    private static Panel Page() => new() { Location = new Point(255, 110), Size = new Size(775, 535), BackColor = Color.Transparent };
    private static VzxCard Card(Point location, Size size) => new() { Location = location, Size = size, BackColor = Color.FromArgb(20, 27, 39), BorderColor = Color.FromArgb(47, 59, 80), BorderSize = 1, BorderRadius = 13 };
    private static VzxDotHeader Header(string text, Point location) => new() { Text = text, Location = location, Size = new Size(250, 25), DotColor = Color.FromArgb(40, 195, 255), TextColor = Color.FromArgb(232, 238, 247), DotSize = 7, PulseGlow = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
    private static Label Label(string text, Point location, Size size, float fontSize, Color color, FontStyle style = FontStyle.Regular) => new() { Text = text, Location = location, Size = size, ForeColor = color, BackColor = Color.Transparent, Font = new Font("Segoe UI", fontSize, style), TextAlign = ContentAlignment.MiddleLeft };
    private static VzxButton Button(string text, Point location, Size size) => new() { Text = text, Location = location, Size = size, BackColor = Color.FromArgb(28, 35, 49), ForeColor = Color.FromArgb(225, 232, 242), BorderRadius = 8, BorderSize = 0, HoverColor = Color.FromArgb(43, 53, 72) };
    private static VzxButton TabButton(string text, Point location) => new() { Text = text, Location = location, Size = new Size(188, 42), BackColor = Color.FromArgb(17, 22, 32), ForeColor = Color.FromArgb(208, 217, 232), BorderRadius = 10, BorderSize = 0, HoverColor = Color.FromArgb(30, 44, 68), PressedColor = Color.FromArgb(37, 55, 84), ActiveIndicatorColor = Color.FromArgb(40, 195, 255), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 0, 0), Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
    private static VzxToggleSwitch Toggle(string text, Point location) => new() { Text = text, Location = location, Size = new Size(155, 27), ForeColor = Color.FromArgb(220, 227, 239), BackColor = Color.Transparent, OnBackColor = Color.FromArgb(21, 164, 224), OnToggleColor = Color.White, OffBackColor = Color.FromArgb(66, 77, 96), OffToggleColor = Color.FromArgb(205, 214, 229), Font = new Font("Segoe UI", 9F) };
    private static VzxTrackBar Slider(Point location, int min, int max) => new() { Location = location, Size = new Size(330, 24), Minimum = min, Maximum = max, Value = min, TrackColor = Color.FromArgb(50, 62, 82), ProgressColor = Color.FromArgb(40, 195, 255), ThumbColor = Color.FromArgb(239, 246, 255), TrackHeight = 5, ThumbSize = 13, ShowDiamondThumb = false };
    private static VzxColorButton ColorButton(Point location) => new() { Location = location, Size = new Size(118, 34), SelectedColor = Color.White, BorderRadius = 8 };
    private static VzxComboBox Combo(Point location) => new() { Location = location, Size = new Size(330, 35), BackColor = Color.FromArgb(27, 35, 49), ForeColor = Color.FromArgb(230, 236, 246), BorderColor = Color.FromArgb(60, 75, 101), BorderFocusColor = Color.FromArgb(40, 195, 255), ArrowColor = Color.FromArgb(40, 195, 255), BorderRadius = 8, BorderSize = 1 };
    private static VzxKeybind Keybind(Point location) => new() { Location = location, Size = new Size(330, 35), BackColor = Color.FromArgb(27, 35, 49), ForeColor = Color.FromArgb(230, 236, 246), BoxBackColor = Color.FromArgb(27, 35, 49), ActiveBorderColor = Color.FromArgb(40, 195, 255), BorderRadius = 8 };
}
