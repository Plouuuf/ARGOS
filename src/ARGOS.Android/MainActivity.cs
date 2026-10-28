using Android.App;
using Android.Content.PM;
using Argos;
using Avalonia.Android;

namespace ArgosDroid;

/// <summary>ARGOS sur Android : une seule vue (<see cref="Argos.Views.ShellView"/>), plein écran.</summary>
[Activity(
    Label = "ARGOS",
    Theme = "@style/ArgosTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = Android.Views.SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode | ConfigChanges.Keyboard |
                           ConfigChanges.KeyboardHidden | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<App>
{
}
