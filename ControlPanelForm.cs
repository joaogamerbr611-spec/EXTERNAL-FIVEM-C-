using System.Numerics;
using VzxWidgets.Controls;

namespace FiveMExternal;

/// <summary>VzxWidgets Windows Forms control panel. All controls remain editable in Visual Studio Designer.</summary>
public partial class ControlPanelForm : VzxForm
{
    private readonly Settings settings;
    private readonly CheatController? cheat;

    public ControlPanelForm() : this(new Settings(), null) { }

    public ControlPanelForm(Settings settings) : this(settings, null) { }

    public ControlPanelForm(Settings settings, CheatController? cheat)
    {
        this.settings = settings;
        this.cheat = cheat;
        InitializeComponent();
        LoadSettings();
        ShowPage(visualPage, visualTab);
    }

    private void LoadSettings()
    {
        espCheckBox.Checked = settings.EspEnabled; boxCheckBox.Checked = settings.EspBox;
        nameCheckBox.Checked = settings.EspName; skeletonCheckBox.Checked = settings.EspSkeleton;
        vehicleCheckBox.Checked = settings.EspVehicles; godModeCheckBox.Checked = settings.GodMode;
        vehicleNameCheckBox.Checked = settings.EspVehicleName; vehicleLockCheckBox.Checked = settings.EspVehicleLock; vehicleDistanceCheckBox.Checked = settings.EspVehicleDistance;
        espDistanceUpDown.Value = Clamp(settings.EspMaxDistance, espDistanceUpDown.Minimum, espDistanceUpDown.Maximum);
        vehicleDistanceUpDown.Value = Clamp(settings.VehicleMaxDistance, vehicleDistanceUpDown.Minimum, vehicleDistanceUpDown.Maximum);
        vehicleUnlockedColorButton.SelectedColor = ToColor(settings.VehicleUnlockedColor); vehicleLockedColorButton.SelectedColor = ToColor(settings.VehicleLockedColor); vehicleInsideColorButton.SelectedColor = ToColor(settings.VehicleInsideColor);
        playerColorButton.SelectedColor = ToColor(settings.PlayerColor); npcColorButton.SelectedColor = ToColor(settings.NpcColor); skeletonColorButton.SelectedColor = ToColor(settings.SkeletonColor);
        aimEnabledCheckBox.Checked = settings.AimEnabled; aimFovCheckBox.Checked = settings.AimShowFov; aimPlayersCheckBox.Checked = settings.AimOnlyPlayers;
        aimPredictionCheckBox.Checked = settings.AimPrediction; aimAlwaysActiveCheckBox.Checked = settings.AimAlwaysActive; aimClosestCheckBox.Checked = settings.AimPrioritizeDistance;
        aimFovUpDown.Value = Clamp(settings.AimFov, aimFovUpDown.Minimum, aimFovUpDown.Maximum); aimSmoothUpDown.Value = Clamp(settings.AimSmooth, aimSmoothUpDown.Minimum, aimSmoothUpDown.Maximum); aimDistanceUpDown.Value = Clamp(settings.AimMaxDistance, aimDistanceUpDown.Minimum, aimDistanceUpDown.Maximum);
        aimBoneComboBox.SelectedIndex = Math.Clamp(settings.AimBone, 0, aimBoneComboBox.Items.Count - 1); aimKeyButton.CurrentKey = (Keys)settings.AimKey;
        silentEnabledCheckBox.Checked = settings.SilentEnabled; silentFovCheckBox.Checked = settings.SilentShowFov; silentPlayersCheckBox.Checked = settings.SilentOnlyPlayers; silentClosestCheckBox.Checked = settings.SilentPrioritizeDistance;
        silentFovUpDown.Value = Clamp(settings.SilentFov, silentFovUpDown.Minimum, silentFovUpDown.Maximum); silentDistanceUpDown.Value = Clamp(settings.SilentMaxDistance, silentDistanceUpDown.Minimum, silentDistanceUpDown.Maximum);
        silentBoneComboBox.SelectedIndex = Math.Clamp(settings.SilentBone, 0, silentBoneComboBox.Items.Count - 1); silentKeyButton.CurrentKey = (Keys)settings.SilentKey;
    }

    private static int Clamp(float value, int minimum, int maximum) => Math.Clamp((int)MathF.Round(value), minimum, maximum);

    private void ShowPage(Panel page, VzxButton tab)
    {
        visualPage.Visible = page == visualPage; aimPage.Visible = page == aimPage; silentPage.Visible = page == silentPage; vehiclePage.Visible = page == vehiclePage;
        foreach (var current in new[] { visualTab, aimTab, silentTab, vehicleTab })
        {
            current.ShowActiveIndicator = current == tab;
            current.BackColor = current == tab ? Color.FromArgb(30, 44, 68) : Color.FromArgb(17, 22, 32);
        }
    }

    private void visualTab_Click(object? sender, EventArgs e) => ShowPage(visualPage, visualTab);
    private void aimTab_Click(object? sender, EventArgs e) => ShowPage(aimPage, aimTab);
    private void silentTab_Click(object? sender, EventArgs e) => ShowPage(silentPage, silentTab);
    private void vehicleTab_Click(object? sender, EventArgs e) => ShowPage(vehiclePage, vehicleTab);
    private void closeButton_Click(object? sender, EventArgs e) => Close();
    private void minimizeButton_Click(object? sender, EventArgs e) => WindowState = FormWindowState.Minimized;
    private void unlockVehicleButton_Click(object? sender, EventArgs e) => SetVehicleActionStatus(cheat?.TryUnlockNearestVehicle() == true ? "Veículo desbloqueado" : "Nenhum veículo em 50 m");
    private void fixVehicleButton_Click(object? sender, EventArgs e) => SetVehicleActionStatus(cheat?.TryFixNearestVehicle() == true ? "Veículo reparado" : "Nenhum veículo em 50 m");
    private void SetVehicleActionStatus(string status) => vehicleActionStatus.Text = status;

    private void espCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.EspEnabled = espCheckBox.Checked;
    private void boxCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.EspBox = boxCheckBox.Checked;
    private void nameCheckBox_CheckedChanged(object? sender, EventArgs e) { settings.EspName = nameCheckBox.Checked; settings.EspDistance = nameCheckBox.Checked; }
    private void skeletonCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.EspSkeleton = skeletonCheckBox.Checked;
    private void vehicleCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.EspVehicles = vehicleCheckBox.Checked;
    private void vehicleNameCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.EspVehicleName = vehicleNameCheckBox.Checked;
    private void vehicleLockCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.EspVehicleLock = vehicleLockCheckBox.Checked;
    private void vehicleDistanceCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.EspVehicleDistance = vehicleDistanceCheckBox.Checked;
    private void godModeCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.GodMode = godModeCheckBox.Checked;
    private void espDistanceUpDown_ValueChanged(object? sender, EventArgs e) => settings.EspMaxDistance = espDistanceUpDown.Value;
    private void vehicleDistanceUpDown_ValueChanged(object? sender, EventArgs e) => settings.VehicleMaxDistance = vehicleDistanceUpDown.Value;
    private void playerColorButton_ColorChanged(object? sender, EventArgs e) => settings.PlayerColor = ToVector(playerColorButton.SelectedColor);
    private void npcColorButton_ColorChanged(object? sender, EventArgs e) => settings.NpcColor = ToVector(npcColorButton.SelectedColor);
    private void skeletonColorButton_ColorChanged(object? sender, EventArgs e) => settings.SkeletonColor = ToVector(skeletonColorButton.SelectedColor);
    private void vehicleUnlockedColorButton_ColorChanged(object? sender, EventArgs e) => settings.VehicleUnlockedColor = ToVector(vehicleUnlockedColorButton.SelectedColor);
    private void vehicleLockedColorButton_ColorChanged(object? sender, EventArgs e) => settings.VehicleLockedColor = ToVector(vehicleLockedColorButton.SelectedColor);
    private void vehicleInsideColorButton_ColorChanged(object? sender, EventArgs e) => settings.VehicleInsideColor = ToVector(vehicleInsideColorButton.SelectedColor);
    private void aimEnabledCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.AimEnabled = aimEnabledCheckBox.Checked;
    private void aimFovCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.AimShowFov = aimFovCheckBox.Checked;
    private void aimPlayersCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.AimOnlyPlayers = aimPlayersCheckBox.Checked;
    private void aimPredictionCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.AimPrediction = aimPredictionCheckBox.Checked;
    private void aimAlwaysActiveCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.AimAlwaysActive = aimAlwaysActiveCheckBox.Checked;
    private void aimClosestCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.AimPrioritizeDistance = aimClosestCheckBox.Checked;
    private void aimFovUpDown_ValueChanged(object? sender, EventArgs e) => settings.AimFov = aimFovUpDown.Value;
    private void aimSmoothUpDown_ValueChanged(object? sender, EventArgs e) => settings.AimSmooth = aimSmoothUpDown.Value;
    private void aimDistanceUpDown_ValueChanged(object? sender, EventArgs e) => settings.AimMaxDistance = aimDistanceUpDown.Value;
    private void aimBoneComboBox_SelectedIndexChanged(object? sender, EventArgs e) { if (aimBoneComboBox.SelectedIndex >= 0) settings.AimBone = aimBoneComboBox.SelectedIndex; }
    private void aimKeyButton_KeyChanged(object? sender, EventArgs e) => settings.AimKey = (int)aimKeyButton.CurrentKey;
    private void silentEnabledCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.SilentEnabled = silentEnabledCheckBox.Checked;
    private void silentFovCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.SilentShowFov = silentFovCheckBox.Checked;
    private void silentPlayersCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.SilentOnlyPlayers = silentPlayersCheckBox.Checked;
    private void silentClosestCheckBox_CheckedChanged(object? sender, EventArgs e) => settings.SilentPrioritizeDistance = silentClosestCheckBox.Checked;
    private void silentFovUpDown_ValueChanged(object? sender, EventArgs e) => settings.SilentFov = silentFovUpDown.Value;
    private void silentDistanceUpDown_ValueChanged(object? sender, EventArgs e) => settings.SilentMaxDistance = silentDistanceUpDown.Value;
    private void silentBoneComboBox_SelectedIndexChanged(object? sender, EventArgs e) { if (silentBoneComboBox.SelectedIndex >= 0) settings.SilentBone = silentBoneComboBox.SelectedIndex; }
    private void silentKeyButton_KeyChanged(object? sender, EventArgs e) => settings.SilentKey = (int)silentKeyButton.CurrentKey;

    private static Vector4 ToVector(Color color) => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    private static Color ToColor(Vector4 value) => Color.FromArgb((int)(value.W * 255), (int)(value.X * 255), (int)(value.Y * 255), (int)(value.Z * 255));
}
