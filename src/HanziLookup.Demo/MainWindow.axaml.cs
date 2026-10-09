using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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
/// <remarks>
/// Three character repositories can be selected: the 9507 Chinese characters, the 72 digits / Latin
/// letters / punctuation, or both merged into one (<see cref="HanziData.Concat"/>).  The case
/// preference exists because the recognizer normalizes every character by its own bounding box -
/// <c>c</c> and <c>C</c> are therefore literally the same geometry and cannot be told apart from the
/// stroke shape; the user decides which one was meant, the same way a shift key would.
/// </remarks>
public partial class MainWindow : Window
{
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));
    private static readonly IBrush Subtle = new SolidColorBrush(Color.FromRgb(0x7A, 0x83, 0x91));
    private static readonly IBrush RowBackground = new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFB));
    private static readonly IBrush SelectedBackground = new SolidColorBrush(Color.FromRgb(0xE3, 0xF0, 0xFD));
    private static readonly IBrush BarTrack = new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xED));

    private HanziData? _chinese;
    private HanziData? _alphanumeric;
    private HanziData? _data;
    private HandwritingSession? _session;
    private CharacterMatch? _selectedMatch;
    private CasePreference _case = CasePreference.AsMatched;
    private string _dataSummary = string.Empty;

    /// <summary>Which repository the matcher searches.</summary>
    private enum CharacterSet
    {
        Chinese,
        Alphanumeric,
        All,
    }

    /// <summary>How a matched Latin letter is reported.</summary>
    private enum CasePreference
    {
        AsMatched,
        Upper,
        Lower,
    }

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

        // Turning this off feeds the analyzer the raw pointer samples. It is worth being able to see
        // the difference: the same stroke then usually produces an order of magnitude more sub-strokes
        // than the character data has, and nothing matches.
        SmoothCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (_session is null)
            {
                return;
            }

            _session.Preprocessing = SmoothCheckBox.IsChecked == true
                ? StrokePreprocessingOptions.Default
                : null;
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
            if (_session is null)
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
            if (_session is null)
            {
                return;
            }

            _session.ResultLimit = value;
            if (_session.HasStrokes)
            {
                _session.Recognize();
            }
        };

        // Lifting the pen is when a character is finished, so that is when the match runs - not on
        // every point, and not after a timer the user cannot see.
        InputCanvas.StrokeCompleted += (_, _) =>
        {
            if (_session is { HasStrokes: true })
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
        var chinesePath = DataFileLocator.Resolve(DataFileLocator.ChineseFileName);
        if (chinesePath is null)
        {
            StatusText.Text =
                "未找到识别数据 data/mmah.json。请从 HanziLookupJS 仓库取得 mmah.json 并放到 data/ 目录，"
                + "或放到程序输出目录的 data/ 子目录下。";
            SetInteractionEnabled(false);
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            _chinese = HanziData.Load(chinesePath);
            stopwatch.Stop();

            _dataSummary = string.Format(
                CultureInfo.InvariantCulture,
                "汉字 {0} 个字符（{1}，加载 {2:0} ms）",
                _chinese.Count,
                DataFileLocator.ChineseFileName,
                stopwatch.Elapsed.TotalMilliseconds);

            var alnumPath = DataFileLocator.Resolve(DataFileLocator.AlphanumericFileName);
            if (alnumPath is not null)
            {
                try
                {
                    _alphanumeric = HanziData.Load(alnumPath);
                    _dataSummary += string.Format(
                        CultureInfo.InvariantCulture,
                        " · 数字与字母 {0} 个字符（{1}）",
                        _alphanumeric.Count,
                        DataFileLocator.AlphanumericFileName);
                }
                catch (Exception ex)
                {
                    _alphanumeric = null;
                    _dataSummary += string.Format(
                        CultureInfo.InvariantCulture,
                        " · {0} 加载失败（{1}），已忽略",
                        DataFileLocator.AlphanumericFileName,
                        ex.GetType().Name);
                }
            }
            else
            {
                _dataSummary += $" · 未找到 {DataFileLocator.AlphanumericFileName}，只识别汉字";
            }

            PopulateSelectors();
            SelectCharacterSet(CharacterSet.Chinese);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加载识别数据失败：{ex.GetType().Name}: {ex.Message}";
            SetInteractionEnabled(false);
        }
    }

    private void PopulateSelectors()
    {
        CharacterSetBox.Items.Clear();
        CharacterSetBox.Items.Add(new ComboBoxItem { Content = "汉字", Tag = CharacterSet.Chinese });
        if (_alphanumeric is not null)
        {
            CharacterSetBox.Items.Add(new ComboBoxItem { Content = "数字 · 字母 · 标点", Tag = CharacterSet.Alphanumeric });
            CharacterSetBox.Items.Add(new ComboBoxItem { Content = "全部（汉字 + 数字字母）", Tag = CharacterSet.All });
        }

        CharacterSetBox.SelectedIndex = 0;

        CaseBox.Items.Clear();
        CaseBox.Items.Add(new ComboBoxItem { Content = "自动", Tag = CasePreference.AsMatched });
        CaseBox.Items.Add(new ComboBoxItem { Content = "大写 A-Z", Tag = CasePreference.Upper });
        CaseBox.Items.Add(new ComboBoxItem { Content = "小写 a-z", Tag = CasePreference.Lower });
        CaseBox.SelectedIndex = 0;
    }

    private void OnCharacterSetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CharacterSetBox.SelectedItem is ComboBoxItem { Tag: CharacterSet set })
        {
            SelectCharacterSet(set);
        }
    }

    private void OnCaseChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CaseBox.SelectedItem is ComboBoxItem { Tag: CasePreference preference })
        {
            _case = preference;
            if (_session is { HasStrokes: true })
            {
                // Only the rendering depends on the case, but re-running keeps one code path.
                RenderResults(_session.Results);
            }
            else
            {
                ResultsPanel.Children.Clear();
                CandidatePreview.Clear();
            }
        }
    }

    private void SelectCharacterSet(CharacterSet set)
    {
        var data = set switch
        {
            CharacterSet.Chinese => _chinese,
            CharacterSet.Alphanumeric => _alphanumeric,
            _ => _chinese is null || _alphanumeric is null
                ? _chinese
                : HanziData.Concat(_chinese, _alphanumeric),
        };

        if (data is null)
        {
            return;
        }

        // Keep what is on the canvas.  Strokes are stored exactly as drawn; the preprocessing
        // pipeline runs at analysis time, so switching repositories needs no conversion.
        var strokes = _session?.Strokes.ToArray() ?? Array.Empty<RawStroke>();

        var session = new HandwritingSession(data, Matcher.DefaultLooseness, (int)ResultCountSlider.Value)
        {
            Preprocessing = SmoothCheckBox.IsChecked == true ? StrokePreprocessingOptions.Default : null,
            Options = StrictCheckBox.IsChecked == true ? MatchOptions.Strict : MatchOptions.JavaScriptCompatible,
        };
        session.Matcher.Looseness = LoosenessSlider.Value;
        session.AutoRecognize = false;
        foreach (var stroke in strokes)
        {
            session.AddStroke(stroke);
        }

        if (_session is not null)
        {
            _session.RecognitionCompleted -= OnRecognitionCompleted;
            _session.Changed -= OnSessionChanged;
        }

        _session = session;
        _session.RecognitionCompleted += OnRecognitionCompleted;
        _session.Changed += OnSessionChanged;
        _data = data;

        InputCanvas.Session = session;
        InputPreview.Data = data;
        CandidatePreview.Data = data;
        _selectedMatch = null;

        if (session.HasStrokes)
        {
            session.Recognize();
        }
        else
        {
            ResultsPanel.Children.Clear();
            CandidatePreview.Clear();
            MetricsText.Text = string.Empty;
            if (session.Analysis is { } analysis && !analysis.IsEmpty)
            {
                InputPreview.ShowAnalysis(analysis);
            }

            StatusText.Text = _dataSummary
                + " · 用鼠标 / 触控笔 / 手指在左侧方格里写字，抬笔即识别。";
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
        SmoothCheckBox.IsEnabled = enabled;
        CharacterSetBox.IsEnabled = enabled;
        CaseBox.IsEnabled = enabled;
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
            Text = ApplyCase(match.Character),
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
        var shown = ApplyCase(match.Character);

        if (_data is not null)
        {
            CandidatePreview.ShowCharacter(_data, shown);
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
            var entry = _data?.Find(shown);
            var subStrokes = entry?.SubStrokeCount;
            StatusText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "候选「{0}」：{1} 笔 / {2} 个子笔画（输入：{3} 笔 / {4} 个子笔画）· 当前字符集 {5} 个字符",
                shown,
                entry?.StrokeCount ?? 0,
                subStrokes ?? 0,
                analysis.StrokeCount,
                analysis.SubStrokeCount,
                _data?.Count ?? 0);
        }
    }

    /// <summary>
    /// Applies the case preference to a Latin letter. Chinese characters and punctuation are
    /// returned unchanged.
    /// </summary>
    private string ApplyCase(string character)
    {
        if (_case == CasePreference.AsMatched || character.Length != 1)
        {
            return character;
        }

        var c = character[0];
        if (!char.IsAsciiLetter(c))
        {
            return character;
        }

        var converted = _case switch
        {
            CasePreference.Upper => char.ToUpperInvariant(c),
            CasePreference.Lower => char.ToLowerInvariant(c),
            _ => c,
        };

        return converted.ToString();
    }
}