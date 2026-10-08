using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HanziLookup.Avalonia;

namespace HanziLookup.Demo;

/// <summary>
/// A command line self check, used by the native AOT job of the CI workflow.
/// </summary>
/// <remarks>
/// <para>
/// Started as <c>HanziLookup.Demo --self-check-ui</c> the program behaves like a normal run - it
/// creates the window from the compiled XAML and loads the character data - but instead of waiting
/// for the user it drives one stroke through the real input canvas, verifies that candidates were
/// produced and rendered, and then shuts the application down with an exit code that reports the
/// outcome. That makes it possible to run the whole application, including the renderer, on a
/// headless CI machine (under <c>xvfb-run</c>) from a native AOT build.
/// </para>
/// </remarks>
internal static class SelfCheck
{
    /// <summary>The command line flag that turns the demo into a self checking run.</summary>
    public const string WindowFlag = "--self-check-ui";

    /// <summary>True when the process was started with <see cref="WindowFlag"/>.</summary>
    public static bool IsWindowCheckRequested =>
        Array.Exists(
            Environment.GetCommandLineArgs(),
            argument => string.Equals(argument, WindowFlag, StringComparison.OrdinalIgnoreCase));

    /// <summary>Runs the checks once the window is up, then shuts the application down.</summary>
    public static void ScheduleWindowCheck(MainWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        Console.WriteLine("self-check-ui: waiting for the window and the character data…");

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            var exitCode = 1;
            try
            {
                exitCode = RunChecks(window);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"self-check-ui FAILED: {exception.GetType().Name}: {exception.Message}");
            }

            if (exitCode == 0)
            {
                Console.WriteLine("self-check-ui passed · window, XAML and recognizer all ran natively");
            }

            desktop.Shutdown(exitCode);
        };
        timer.Start();
    }

    private static int RunChecks(MainWindow window)
    {
        var failures = 0;

        var status = window.GetControl<TextBlock>("StatusText").Text ?? string.Empty;
        Report("data file loaded", status.Contains("9507", StringComparison.Ordinal), status, ref failures);
        Report(
            "window is visible and sized",
            window.IsVisible && window.Bounds.Width > 100 && window.Bounds.Height > 100,
            $"{window.IsVisible} · {window.Bounds}",
            ref failures);

        // Drive the input canvas the same way the pointer handlers do when a user draws.
        var canvas = window.GetControl<StrokeInputCanvas>("InputCanvas");
        canvas.BeginStroke(new StrokePoint(60, 90));
        for (var step = 1; step <= 10; step++)
        {
            canvas.ExtendStroke(new StrokePoint(60 + step * 18, 90));
        }

        canvas.EndStroke();

        var session = canvas.Session;
        Report(
            "stroke analysed",
            session?.Analysis is { StrokeCount: 1 },
            $"{session?.Analysis?.SubStrokeCount ?? 0} sub-strokes",
            ref failures);
        Report(
            "candidates matched",
            session is { Results.Count: > 0 },
            session is null ? "no session" : $"best: {session.Results[0]}",
            ref failures);
        Report(
            "candidates rendered",
            window.GetControl<StackPanel>("ResultsPanel").Children.Count > 0,
            window.GetControl<StackPanel>("ResultsPanel").Children.Count.ToString(CultureInfo.InvariantCulture),
            ref failures);

        var metrics = window.GetControl<TextBlock>("MetricsText").Text ?? string.Empty;
        Report("metrics updated", metrics.Length > 0, metrics, ref failures);

        return failures == 0 ? 0 : 1;
    }

    private static void Report(string name, bool ok, string detail, ref int failures)
    {
        if (ok)
        {
            Console.WriteLine($"  ok   {name}: {detail}");
            return;
        }

        failures++;
        Console.Error.WriteLine($"  FAIL {name}: {detail}");
    }
}
