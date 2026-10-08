using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using HanziLookup.Avalonia;
using Xunit;

namespace HanziLookup.Demo.Tests;

/// <summary>
/// Smoke test for the demo application. It runs the real <see cref="MainWindow"/> on the headless
/// Avalonia platform, so everything the XAML cannot verify is covered here: the data file is found
/// at runtime, pointer input reaches the input canvas, a finished stroke is analysed and matched,
/// the candidate rows are rendered, and one render pass runs the whole drawing code.
/// </summary>
public sealed class DemoSmokeTests
{
    [Fact]
    public async Task The_window_loads_the_data_file_draws_a_stroke_and_recognizes_it()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(App));

        await session.Dispatch(
            () =>
            {
                var window = new MainWindow { Width = 1280, Height = 900 };
                window.Show();
                Dispatcher.UIThread.RunJobs(); // run the first layout pass, so bounds are known

                var canvas = window.GetControl<StrokeInputCanvas>("InputCanvas");
                var handwriting = canvas.Session;
                Assert.NotNull(handwriting); // the character data was loaded
                Assert.Empty(handwriting!.Strokes);

                var metrics = window.GetControl<TextBlock>("MetricsText");
                var status = window.GetControl<TextBlock>("StatusText");
                Assert.Contains("9507", status.Text ?? string.Empty);

                // Draw a horizontal stroke across the input canvas, exactly like a user would.
                var origin = canvas.TranslatePoint(default, window);
                Assert.NotNull(origin);
                var start = origin!.Value + new Vector(60, 90);
                window.MouseDown(start, MouseButton.Left);
                for (var step = 1; step <= 10; step++)
                {
                    window.MouseMove(start + new Vector(step * 24, 0));
                }

                window.MouseUp(start + new Vector(240, 0), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.Single(handwriting.Strokes);
                var analysis = handwriting.Analysis;
                Assert.NotNull(analysis);
                Assert.Equal(1, analysis!.StrokeCount);
                Assert.NotEmpty(handwriting.Results);

                var results = window.GetControl<StackPanel>("ResultsPanel");
                Assert.NotEmpty(results.Children); // the candidate rows were built
                Assert.False(string.IsNullOrWhiteSpace(metrics.Text));

                // The replay animation of the demo ("simulated writing") toggles without a display.
                canvas.StartPlayback();
                Assert.True(canvas.IsPlaying);
                canvas.StopPlayback();
                Assert.False(canvas.IsPlaying);

                // Forces a compositor frame, which runs Render() of the window, the input canvas,
                // both stroke previews and the analysis renderer inside them.
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();

                // Clearing through the real button also proves hit testing and the event wiring.
                var clear = window.GetControl<Button>("ClearButton");
                var clearOrigin = clear.TranslatePoint(default, window);
                Assert.NotNull(clearOrigin);
                var middle = clearOrigin!.Value +
                             new Vector(clear.Bounds.Width / 2, clear.Bounds.Height / 2);
                window.MouseDown(middle, MouseButton.Left);
                window.MouseUp(middle, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.Empty(handwriting.Strokes);
                Assert.Empty(results.Children);
                Assert.Null(handwriting.Analysis);
            },
            CancellationToken.None);
    }
}
