using AspectSlide.Catalog;
using AspectSlide.Model;
using AspectSlide.Slideshow;
using AspectSlide.Windows;
using System.Diagnostics;

namespace AspectSlide.UI;

public sealed class SettingsForm : Form
{
    private readonly AppSettings settings;
    private readonly SettingsStore store;
    private readonly SlideshowController controller;
    private readonly StartupShortcut startup;
    private readonly IWallpaperPlatform platform;
    private readonly Action exit;
    private readonly TextBox folderText = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly NumericUpDown minutes = new() { Minimum = 1, Maximum = 1440, Dock = DockStyle.Left, Width = 90 };
    private readonly ComboBox fit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Left, Width = 100 };
    private readonly CheckBox signIn = new() { Text = "Start at sign-in", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(510, 0) };
    private readonly ListBox monitors = new() { Dock = DockStyle.Fill, Height = 105 };
    private readonly Button pause = new() { AutoSize = true };
    private bool updatingSignIn;

    public SettingsForm(AppSettings settings, SettingsStore store, SlideshowController controller,
        StartupShortcut startup, IWallpaperPlatform platform, Action exit)
    {
        this.settings = settings;
        this.store = store;
        this.controller = controller;
        this.startup = startup;
        this.platform = platform;
        this.exit = exit;
        Text = "AspectSlide settings";
        MinimumSize = new Size(570, 470);
        Size = new Size(660, 530);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.Controls.Add(folderText, 0, 0);
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += async (_, _) => await ChooseFolderAsync();
        folderRow.Controls.Add(browse, 1, 0);
        AddRow(layout, 0, "Photo folder", folderRow);

        AddRow(layout, 1, "Change every", minutes);
        var intervalHint = new Label { Text = "minutes (1–1440)", AutoSize = true };
        layout.Controls.Add(intervalHint, 1, 2);
        AddRow(layout, 3, "Display mode", fit);
        layout.Controls.Add(signIn, 1, 4);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        AddButton(actions, "Change now", ChangeNowWithWarningAsync);
        pause.Click += (_, _) => { controller.SetPaused(!settings.Paused); RefreshStatus(); };
        actions.Controls.Add(pause);
        AddButton(actions, "Check again", ChangeNowWithWarningAsync);
        AddButton(actions, "Restore previous", async () => await controller.RestorePreviousAsync());
        AddButton(actions, "Windows colors", () => OpenWindowsSettings("ms-settings:colors"));
        AddButton(actions, "Windows background", () => OpenWindowsSettings("ms-settings:personalization-background"));
        AddButton(actions, "Exit", () => { exit(); return Task.CompletedTask; });
        layout.Controls.Add(actions, 0, 5);
        layout.SetColumnSpan(actions, 2);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, RowCount = 3 };
        bottom.Controls.Add(new Label { Text = "Monitors", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        bottom.Controls.Add(monitors, 0, 1);
        bottom.Controls.Add(status, 0, 2);
        layout.Controls.Add(bottom, 0, 6);
        layout.SetColumnSpan(bottom, 2);

        folderText.Text = settings.Folder ?? "No folder chosen";
        minutes.Value = Math.Clamp((decimal)settings.Interval.TotalMinutes, 1, 1440);
        minutes.ValueChanged += (_, _) =>
        {
            try { controller.UpdateInterval(TimeSpan.FromMinutes((double)minutes.Value)); }
            catch (Exception ex) { ShowError(ex); }
        };
        fit.Items.AddRange(["Fill", "Fit"]);
        fit.SelectedItem = settings.Fit.ToString();
        fit.SelectedIndexChanged += (_, _) =>
        {
            settings.Fit = fit.SelectedIndex == 1 ? WallpaperFit.Fit : WallpaperFit.Fill;
            store.Save(settings);
        };
        signIn.Checked = settings.StartAtSignIn;
        signIn.CheckedChanged += (_, _) =>
        {
            if (updatingSignIn) return;
            try { startup.SetEnabled(signIn.Checked); settings.StartAtSignIn = signIn.Checked; store.Save(settings); }
            catch (Exception ex)
            {
                updatingSignIn = true;
                signIn.Checked = settings.StartAtSignIn;
                updatingSignIn = false;
                ShowError(ex);
            }
        };
        FormClosing += (_, args) =>
        {
            if (args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); }
        };
        RefreshStatus();
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control value)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) }, 0, row);
        layout.Controls.Add(value, 1, row);
    }

    private void AddButton(FlowLayoutPanel layout, string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += async (_, _) =>
        {
            try { await action(); RefreshStatus(); }
            catch (Exception ex) { ShowError(ex); }
        };
        layout.Controls.Add(button);
    }

    private async Task ChooseFolderAsync()
    {
        using var picker = new FolderBrowserDialog { Description = "Choose your local photo folder", UseDescriptionForTitle = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string path = LocalFolderValidator.Validate(picker.SelectedPath);
            if (!settings.OriginalCaptured && platform.GetAttachedDisplays().Any(d => string.IsNullOrEmpty(platform.GetWallpaper(d.Id))))
            {
                var proceed = MessageBox.Show(this,
                    "A monitor currently uses a solid-color background. AspectSlide cannot restore that color automatically. Continue?",
                    "Restore limitation", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (proceed != DialogResult.OK) return;
                controller.AcknowledgeSolidColorRestoreLimitation();
            }
            settings.Folder = path;
            folderText.Text = path;
            store.Save(settings);
            await controller.StartAsync();
            RefreshStatus();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    public void RefreshStatus()
    {
        if (IsDisposed) return;
        pause.Text = settings.Paused ? "Resume" : "Pause";
        status.Text = controller.Status;
        monitors.Items.Clear();
        try
        {
            foreach (var display in platform.GetAttachedDisplays())
            {
                string path = platform.GetWallpaper(display.Id);
                monitors.Items.Add($"{display.Width} × {display.Height}  —  {(string.IsNullOrEmpty(path) ? "Solid color" : Path.GetFileName(path))}");
            }
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private async Task ChangeNowWithWarningAsync()
    {
        if (!settings.OriginalCaptured && !settings.SolidColorAcknowledged &&
            platform.GetAttachedDisplays().Any(d => string.IsNullOrEmpty(platform.GetWallpaper(d.Id))))
        {
            var proceed = MessageBox.Show(this,
                "A monitor currently uses a solid-color background. AspectSlide cannot restore that color automatically. Continue?",
                "Restore limitation", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (proceed != DialogResult.OK) return;
            controller.AcknowledgeSolidColorRestoreLimitation();
        }
        await controller.ChangeNowAsync();
    }

    private static Task OpenWindowsSettings(string uri)
    {
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "AspectSlide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
