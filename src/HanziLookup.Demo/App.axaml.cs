using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace HanziLookup.Demo;

/// <summary>The demo application.</summary>
public partial class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            if (SelfCheck.IsWindowCheckRequested)
            {
                SelfCheck.ScheduleWindowCheck(window, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
