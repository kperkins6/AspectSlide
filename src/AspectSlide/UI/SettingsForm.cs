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
    private readonly Func<string, Task> launchWindowsSettings;
    private readonly Func<IWin32Window, string?> selectFolder;
    private readonly Action<Exception> reportError;
    private readonly TextBox folderText = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly NumericUpDown minutes = new() { Minimum = 1, Maximum = 1440, Dock = DockStyle.Left, Width = 90 };
    private readonly ComboBox fit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Left, Width = 100 };
    private readonly CheckBox signIn = new() { Text = "Start at sign-in", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(510, 0) };
    private readonly Label libraryCount = new() { AutoSize = true };
    private readonly FlowLayoutPanel monitors = new() { Dock = DockStyle.Fill, Height = 165, AutoScroll = true };
    private readonly Button pause = new() { AutoSize = true };
    private bool updatingSignIn;

    public SettingsForm(AppSettings settings, SettingsStore store, SlideshowController controller,
        StartupShortcut startup, IWallpaperPlatform platform, Action exit,
        Func<string, Task>? launchWindowsSettings = null, Func<IWin32Window, string?>? selectFolder = null,
        Action<Exception>? reportError = null)
    {
        this.settings = settings;
        this.store = store;
        this.controller = controller;
        this.startup = startup;
        this.platform = platform;
        this.exit = exit;
        this.launchWindowsSettings = launchWindowsSettings ?? OpenWindowsSettings;
        this.selectFolder = selectFolder ?? PickFolder;
        this.reportError = reportError ?? (ex => MessageBox.Show(this, ex.Message, "AspectSlide", MessageBoxButtons.OK, MessageBoxIcon.Warning));
        Text = "AspectSlide settings";
        MinimumSize = new Size(570, 530);
        Size = new Size(660, 600);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, AutoSize = true };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.Controls.Add(folderText, 0, 0);
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += async (_, _) => await ChooseFolderAsync();
        folderRow.Controls.Add(browse, 1, 0);
        folderRow.Controls.Add(libraryCount, 0, 1);
        folderRow.SetColumnSpan(libraryCount, 2);
        AddRow(layout, 0, "Photo folder", folderRow);

        AddRow(layout, 1, "Change every", minutes);
        var intervalHint = new Label { Text = "minutes (1–1440)", AutoSize = true };
        layout.Controls.Add(intervalHint, 1, 2);
        AddRow(layout, 3, "Display mode", fit);
        layout.Controls.Add(signIn, 1, 4);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        AddButton(actions, "Change now", ChangeNowWithWarningAsync);
        pause.Click += (_, _) =>
        {
            try { controller.SetPaused(!settings.Paused); RefreshStatus(); }
            catch (Exception ex) { ShowError(ex); }
        };
        actions.Controls.Add(pause);
        AddButton(actions, "Check again", ChangeNowWithWarningAsync);
        AddButton(actions, "Restore previous", async () => await controller.RestorePreviousAsync());
        AddButton(actions, "Windows colors", () => this.launchWindowsSettings("ms-settings:colors"));
        AddButton(actions, "Windows background", () => this.launchWindowsSettings("ms-settings:personalization-background"));
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
            try { settings.Fit = fit.SelectedIndex == 1 ? WallpaperFit.Fit : WallpaperFit.Fill; store.Save(settings); }
            catch (Exception ex) { ShowError(ex); }
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
        VisibleChanged += (_, _) =>
        {
            if (Visible) RefreshStatus();
            else DisposePreviews();
        };
        FormClosed += (_, _) => DisposePreviews();
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
        try
        {
            string? selected = selectFolder(this);
            if (selected is null) return;
            string path = LocalFolderValidator.Validate(selected);
            if (!settings.OriginalCaptured && platform.GetAttachedDisplays().Any(d => string.IsNullOrEmpty(platform.GetWallpaper(d.Id))))
            {
                var proceed = MessageBox.Show(this,
                    "A monitor currently uses a solid-color background. AspectSlide cannot restore that color automatically. Continue?",
                    "Restore limitation", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (proceed != DialogResult.OK) return;
                controller.AcknowledgeSolidColorRestoreLimitation();
            }
            await controller.SelectFolderAsync(path);
            folderText.Text = path;
            RefreshStatus();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private static string? PickFolder(IWin32Window owner)
    {
        using var picker = new FolderBrowserDialog { Description = "Choose your local photo folder", UseDescriptionForTitle = true };
        return picker.ShowDialog(owner) == DialogResult.OK ? picker.SelectedPath : null;
    }

    public void RefreshStatus()
    {
        if (IsDisposed) return;
        pause.Text = settings.Paused ? "Resume" : "Pause";
        status.Text = controller.Status;
        libraryCount.Text = $"{controller.LibraryCount} usable photo(s), including subfolders";
        DisposePreviews();
        if (!Visible) return;
        try
        {
            int number = 0;
            foreach (var display in platform.GetAttachedDisplays())
            {
                number++;
                string path = platform.GetWallpaper(display.Id);
                string? source = controller.GetCurrentSourcePath(display.Id, path);
                var card = new TableLayoutPanel { Width = 185, Height = 145, RowCount = 2, Margin = new Padding(3, 3, 8, 3) };
                card.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
                card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                var preview = new PictureBox { Width = 155, Height = 82, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(40, 40, 40) };
                preview.Image = CreateThumbnail(path);
                card.Controls.Add(preview, 0, 0);
                string orientation = display.Width > display.Height ? "Landscape" : display.Height > display.Width ? "Portrait" : "Square";
                card.Controls.Add(new Label
                {
                    AutoSize = true,
                    MaximumSize = new Size(180, 0),
                    Text = $"Monitor {number} · {orientation} · {display.Width} × {display.Height}\n" +
                           (string.IsNullOrEmpty(path) ? "Solid color" : Path.GetFileName(source ?? path))
                }, 0, 1);
                monitors.Controls.Add(card);
            }
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private static Image? CreateThumbnail(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            string? linkTarget = (attributes & FileAttributes.ReparsePoint) != 0 ? new FileInfo(path).LinkTarget : null;
            if (!PhotoCatalog.ShouldTraverse(attributes, linkTarget)) return null;
            using var original = Image.FromFile(path);
            double scale = Math.Min(155.0 / original.Width, 82.0 / original.Height);
            return new Bitmap(original, new Size(Math.Max(1, (int)(original.Width * scale)),
                Math.Max(1, (int)(original.Height * scale))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        { return null; }
    }

    private void DisposePreviews()
    {
        if (Disposing || IsDisposed || monitors.Disposing || monitors.IsDisposed) return;
        foreach (Control card in monitors.Controls.Cast<Control>().ToArray())
        {
            if (card.Disposing || card.IsDisposed) continue;
            if (card.Parent == monitors) monitors.Controls.Remove(card);
            foreach (var preview in card.Controls.OfType<PictureBox>())
            {
                Image? image = preview.Image;
                preview.Image = null;
                image?.Dispose();
            }
            card.Dispose();
        }
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

    private void ShowError(Exception ex) => reportError(ex);
}
