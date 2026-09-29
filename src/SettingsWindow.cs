using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.UI;

namespace ClickShow;

internal sealed class SettingsWindow : Window
{
    private readonly App app;
    private readonly ToggleSwitch enabled = new() { Header = "启用", OnContent = "", OffContent = "" };
    private readonly ToggleSwitch startup = new() { Header = "开机自启", OnContent = "", OffContent = "" };
    private readonly ToggleSwitch elevated = new() { Header = "管理员权限", OnContent = "", OffContent = "" };
    private readonly Button restart = new() { Content = "重启应用", VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider diameter = new() { Minimum = 50, Maximum = 200, StepFrequency = 5, TickFrequency = 25, Header = "波纹大小", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox easing = new() { Header = "动画", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleSwitch[] buttons = new ToggleSwitch[5];
    private readonly Button[] colors = new Button[5];
    private bool refreshing;
    public SettingsWindow(App app)
    {
        this.app = app; Title = "ClickShow 设置";
        SystemBackdrop = new MicaBackdrop();
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(24), MaxWidth = 1800, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(new TextBlock { Text = "ClickShow 1.0.0", FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var columns = new Grid { ColumnSpacing = 100, RowSpacing = 24 };
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        columns.RowDefinitions.Add(new() { Height = GridLength.Auto });
        columns.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var general = new StackPanel { Spacing = 15 };
        general.Children.Add(new TextBlock { Text = "常规", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        general.Children.Add(enabled); general.Children.Add(startup);
        var permissionRow = new Grid { ColumnSpacing = 16 };
        permissionRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); permissionRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        permissionRow.Children.Add(elevated); Grid.SetColumn(restart, 1); permissionRow.Children.Add(restart); general.Children.Add(permissionRow);
        general.Children.Add(diameter);
        foreach (string name in new[] { "线性", "缓入", "缓出", "缓入缓出" }) easing.Items.Add(name);
        general.Children.Add(easing);
        columns.Children.Add(general);
        var appearance = new StackPanel { Spacing = 15 };
        appearance.Children.Add(new TextBlock { Text = "按键", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        string[] names = ["左键", "中键", "右键", "侧键(后退)", "侧键(前进)"];
        // 只调整显示顺序，保留配置索引。
        foreach (int i in new[] { 0, 1, 2, 4, 3 })
        {
            int index = i;
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            buttons[i] = new() { Header = names[i], OnContent = "", OffContent = "" }; colors[i] = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 132 };
            AutomationProperties.SetName(colors[i], names[i] + "颜色");
            row.Children.Add(buttons[i]); Grid.SetColumn(colors[i], 1); row.Children.Add(colors[i]); appearance.Children.Add(row);
            buttons[i].Toggled += (_, _) => { if (refreshing) return; var values = app.Settings.Buttons.ToArray(); values[index] = values[index] with { Enabled = buttons[index].IsOn }; app.Change(app.Settings with { Buttons = values }); };
            colors[i].Click += (_, _) => OpenColor(index, names[index]);
        }
        Grid.SetColumn(appearance, 1); columns.Children.Add(appearance); panel.Children.Add(columns);
        columns.SizeChanged += (_, args) =>
        {
            bool narrow = args.NewSize.Width < 620;
            columns.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            columns.ColumnSpacing = narrow ? 0 : 100;
            Grid.SetColumn(appearance, narrow ? 0 : 1);
            Grid.SetRow(appearance, narrow ? 1 : 0);
        };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int width = Math.Min(1800, work.Width - 32), height = Math.Min(1200, work.Height - 32);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ClickShow.ico"));
        AppWindow.Closing += (_, args) => { args.Cancel = true; AppWindow.Hide(); };
        enabled.Toggled += (_, _) => { if (!refreshing) app.Change(app.Settings with { Enabled = enabled.IsOn }); };
        diameter.ValueChanged += (_, _) => { if (!refreshing) app.Change(app.Settings with { Diameter = (int)(Math.Round(diameter.Value / 5) * 5) }); };
        easing.SelectionChanged += (_, _) => { if (!refreshing) app.Change(app.Settings with { Easing = (RippleEasing)easing.SelectedIndex }); };
        startup.Toggled += async (_, _) => { if (!refreshing) await ApplyStartup(); };
        elevated.Toggled += async (_, _) => { if (!refreshing) await ApplyStartup(); };
        restart.Click += async (_, _) => { restart.IsEnabled = false; try { await app.Restart(); } catch (Exception ex) { app.Report(ex.Message); } finally { restart.IsEnabled = true; Refresh(); } };
        Refresh();
    }
    private async Task ApplyStartup()
    {
        startup.IsEnabled = elevated.IsEnabled = false;
        try { await app.ChangeStartup(startup.IsOn, elevated.IsOn); }
        catch (Exception ex) { app.Report(ex.Message); }
        finally { startup.IsEnabled = elevated.IsEnabled = true; Refresh(); }
    }
    public void Refresh()
    {
        refreshing = true;
        var settings = app.Settings;
        enabled.IsOn = settings.Enabled; startup.IsOn = settings.Startup; elevated.IsOn = settings.Elevated;
        restart.Visibility = settings.Elevated == Privileges.IsElevated ? Visibility.Collapsed : Visibility.Visible;
        diameter.Value = settings.Diameter; easing.SelectedIndex = (int)settings.Easing;
        // 只调整显示顺序，保留配置索引。
        foreach (int i in new[] { 0, 1, 2, 4, 3 })
        {
            buttons[i].IsOn = settings.Buttons[i].Enabled;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            content.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Parse(settings.Buttons[i].Color)) });
            content.Children.Add(new TextBlock { Text = settings.Buttons[i].Color }); colors[i].Content = content;
        }
        refreshing = false;
    }
    private static Color Parse(string value) { uint rgb = Convert.ToUInt32(value[1..], 16); return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); }
    private void OpenColor(int index, string name)
    {
        var picker = new ColorPicker { Color = Parse(app.Settings.Buttons[index].Color), IsAlphaEnabled = false, IsAlphaSliderVisible = false, IsAlphaTextInputVisible = false, IsHexInputVisible = true, IsColorChannelTextInputVisible = true };
        AutomationProperties.SetName(picker, name + "颜色选择");
        picker.ColorChanged += (_, args) =>
        {
            var values = app.Settings.Buttons.ToArray(); values[index] = values[index] with { Color = $"#{args.NewColor.R:X2}{args.NewColor.G:X2}{args.NewColor.B:X2}" };
            app.Change(app.Settings with { Buttons = values });
        };
        var flyout = new Flyout { Content = picker }; flyout.ShowAt(colors[index]);
    }
}
