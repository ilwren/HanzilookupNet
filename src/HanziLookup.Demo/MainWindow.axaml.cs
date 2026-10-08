using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HanziLookup;
using HanziLookup.Avalonia;

namespace HanziLookup.Demo;

/// <summary>
/// The demo window: a stroke input canvas on the left, the recognition results on the right, and two
/// sub-stroke skeleton previews (the analysis of the input and the skeleton of the selected
/// candidate) at the bottom right.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));
    private static readonly IBrush Subtle = new SolidColorBrush(Color.FromRgb(0x7A, 0x83, 0x91));
    private static readonly IBrush RowBackground = new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFB));
    private static readonly IBrush SelectedBackground = new SolidColorBrush(Color.FromRgb(0xE3, 0xF0, 0xFD));
    private static readonly IBrush BarTrack = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED));

    private HanziData? _data;
    private HandwritingSession? _session;
    private CharacterMatch? _selectedMatch;
    private bool _updatingUi;

    /// <summary>Creates the main window.</summary>
    public MainWindow()
    {
        InitializeComponent();
        WireEvents();
        LoadCharacterData();
    }

    private void WireEvents()
    {
        ClearButton.Click += (_, _) =>
        {
            _selectedMatch = null;
            InputCanvas.Clear();
            CandidatePreview.Clear();
            ResultsPanel.Children.Clear();
            MetricsText.Text = string.Empty;
        };

        UndoButton.Click += (_, _) =>
        {
            _selectedMatch = null;
            CandidatePreview.Clear();
            InputCanvas.Undo();
        };

        PlayButton.Click += (_, _) => InputCanvas.StartPlayback();
        StopButton.Click += (_, _) => InputCanvas.StopPlayback();

        GuidesCheckBox.IsCheckedChanged += (_, _) =>
        {
            var on = GuidesCheckBox.IsChecked ?? false;
            InputCanvas.ShowGuides = on;
            InputPreview.ShowGuides = on;
            CandidatePreview.ShowGuides = on;
        };

        AnalysisCheckBox.IsCheckedChanged += (_, _) => InputCanvas.ShowAnalysis = AnalysisCheckBox.IsChecked ?? false;

        StrictCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (_session is null)
            {
                return;
            }

            _session.Options = StrictCheckBox.IsChecked == true ? MatchOptions.Strict : MatchOptions.JavaScriptCompatible;
            if (_session.HasStrokes)
            {
                _session.Recognize();
            }
        };

        LoosenessSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty)
            {
                return;
            }

            var value = LoosenessSlider.Value;
            LoosenessText.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
            if (_session is null || _updatingUi)
            {
                return;
            }

            // The setter (unlike the constructor) takes 0 literally, i.e. "require the exact number of strokes".
            _session.Matcher.Looseness = value;
            if (_session.HasStrokes)
            {
                _session.Recognize();
            }
        };

        ResultCountSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty)
            {
                return;
            }

            var value = (int)Math.Round(ResultCountSlider.Value);
            ResultCountText.Text = value.ToString(CultureInfo.InvariantCulture);
            if (_session is null || _updatingUi)
            {
                return;
            }

            _session.ResultLimit = value;
            if (_session.HasStrokes)
            {
                _session.Recognize();
            }
        };

        InputCanvas.PlaybackCompleted += (_, _) =>
            MetricsText.Text = "笔画重放完成（这就是“模拟书写”：逐点重绘捕获到的轨迹）。";

        InputCanvas.CurrentStrokeChanged += (_, _) =>
        {
            // Live preview of the in-progress stroke: analyse what has been drawn so far.
            if (InputCanvas.CurrentStroke is { Count: > 1 } current)
            {
                InputPreview.ShowAnalysis(AnalyzedCharacter.FromStrokes(new[] { current }));
            }
        };
    }

    private void LoadCharacterData()
    {
        var path = DataFileLocator.Resolve();
        if (path is null)
        {
            StatusText.Text =
                "未找到识别数据 data/mmah.json。请从 HanziLookupJS 仓库取得 mmah.json 并放到 data/ 目录，" +
                "或放到程序输出目录的 data/ 子目录下。";
            SetInteractionEnabled(false);
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            _data = HanziData.Load(path);
            stopwatch.Stop();

            _session = new HandwritingSession(_data, Matcher.DefaultLooseness, (int)ResultCountSlider.Value);
            _session.RecognitionCompleted += OnRecognitionCompleted;
            _session.Changed += OnSessionChanged;

            InputCanvas.Session = _session;
            InputPreview.Data = _data;
            CandidatePreview.Data = _data;

            StatusText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "数据：{0} 个字符（{1}）· 加载用时 {2:0} ms · 用鼠标 / 触控笔 / 手指在左侧方格里写字，抬笔即识别。",
                _data.Count,
                System.IO.Path.GetFileName(path),
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加载识别数据失败：{ex.GetType().Name}: {ex.Message}";
            SetInteractionEnabled(false);
        }
    }

    private void SetInteractionEnabled(bool enabled)
    {
        ClearButton.IsEnabled = enabled;
        UndoButton.IsEnabled = enabled;
        PlayButton.IsEnabled = enabled;
        StopButton.IsEnabled = enabled;
        LoosenessSlider.IsEnabled = enabled;
        ResultCountSlider.IsEnabled = enabled;
        StrictCheckBox.IsEnabled = enabled;
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        var analysis = _session.Analysis;
        if (analysis is null || analysis.IsEmpty)
        {
            InputPreview.Clear();
        }
        else
        {
            InputPreview.ShowAnalysis(analysis);
        }
    }

    private void OnRecognitionCompleted(object? sender, RecognitionCompletedEventArgs e)
    {
        MetricsText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "输入：{0} 笔 / {1} 个子笔画 · 比较：{2:n0} 个候选字、{3:n0} 次子笔画比较 · 用时 {4:0.0} ms",
            e.Analysis.StrokeCount,
            e.Analysis.SubStrokeCount,
            e.Counters.CharactersChecked,
            e.Counters.SubStrokesCompared,
            e.Duration.TotalMilliseconds);

        RenderResults(e.Results);
    }

    private void RenderResults(System.Collections.Generic.IReadOnlyList<CharacterMatch> results)
    {
        ResultsPanel.Children.Clear();

        if (results.Count == 0)
        {
            ResultsPanel.Children.Add(new TextBlock
            {
                Text = "还没有候选结果。写几笔试试。",
                Foreground = Subtle,
                FontSize = 12,
                Margin = new Thickness(4)
            });
            CandidatePreview.Clear();
            return;
        }

        var best = results[0].Score;
        for (var i = 0; i < results.Count; ++i)
        {
            ResultsPanel.Children.Add(CreateResultRow(results[i], i, best));
        }

        // Preselect the best candidate so the skeleton preview is never empty.
        SelectCandidate(results[0], null);
    }

    private Control CreateResultRow(CharacterMatch match, int index, double bestScore)
    {
        var ratio = double.IsFinite(match.Score) && bestScore > 0
            ? Math.Clamp(match.Score / bestScore, 0, 1)
            : 0;

        var glyph = new TextBlock
        {
            Text = match.Character,
            FontSize = 34,
            Width = 44,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var scoreTrack = new Border
        {
            Width = 170,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = BarTrack,
            VerticalAlignment = VerticalAlignment.Center
        };

        var scoreBar = new Border
        {
            Width = Math.Max(2, 170 * ratio),
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = index == 0 ? Accent : new SolidColorBrush(Color.FromRgb(0x7F, 0xB1, 0xE8)),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        scoreTrack.Child = scoreBar;

        var scoreText = new TextBlock
        {
            Text = match.HasFiniteScore
                ? match.Score.ToString("0.###", CultureInfo.InvariantCulture)
                : "未比较",
            FontSize = 12,
            Width = 74,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = match.HasFiniteScore ? Subtle : new SolidColorBrush(Color.FromRgb(0xC5, 0x2A, 0x2A))
        };

        var detail = new TextBlock
        {
            Text = match.HasFiniteScore ? $"#{index + 1}" : $"#{index + 1} · JS 兼容行为",
            FontSize = 10,
            Width = 96,
            Foreground = Subtle,
            VerticalAlignment = VerticalAlignment.Center
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { glyph, detail, scoreTrack, scoreText }
        };

        var row = new Border
        {
            Background = RowBackground,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4),
            Child = content,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        row.PointerPressed += (_, _) => SelectCandidate(match, row);
        return row;
    }

    private void SelectCandidate(CharacterMatch match, Border? row)
    {
        _selectedMatch = match;

        if (_data is not null)
        {
            CandidatePreview.ShowCharacter(_data, match.Character);
        }

        foreach (var child in ResultsPanel.Children)
        {
            if (child is Border border && border.Background != BarTrack)
            {
                border.Background = RowBackground;
            }
        }

        if (row is not null)
        {
            row.Background = SelectedBackground;
        }

        if (_selectedMatch is not null && _session?.Analysis is { } analysis)
        {
            var entry = _data?.Find(match.Character);
            var subStrokes = entry?.SubStrokeCount;
            StatusText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "候选「{0}」：{1} 笔 / {2} 个子笔画（输入：{3} 笔 / {4} 个子笔画）· 数据文件：{5} 个字符",
                match.Character,
                entry?.StrokeCount ?? 0,
                subStrokes ?? 0,
                analysis.StrokeCount,
                analysis.SubStrokeCount,
                _data?.Count ?? 0);
        }
    }
}
