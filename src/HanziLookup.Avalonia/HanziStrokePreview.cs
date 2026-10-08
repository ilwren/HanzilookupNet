using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HanziLookup;

namespace HanziLookup.Avalonia;

/// <summary>
/// Shows the sub-stroke skeleton that the recognizer sees: either the analysis of a captured
/// character, or a character of the data repository (a recognition candidate) reconstructed from its
/// stored sub-stroke descriptors.
/// </summary>
/// <remarks>
/// This is the "simulated" view of a character: it does not need the original strokes (and does not
/// need a font with the glyph) - the quantized sub-strokes are expanded back into segments and
/// drawn. It is what makes it possible to show <em>why</em> a candidate was matched, and to preview
/// candidates whose glyph would otherwise have to be rendered as text.
/// </remarks>
public sealed class HanziStrokePreview : Control
{
    /// <summary>Identifies the <see cref="Data"/> property.</summary>
    public static readonly StyledProperty<HanziData?> DataProperty =
        AvaloniaProperty.Register<HanziStrokePreview, HanziData?>(nameof(Data));

    /// <summary>Identifies the <see cref="Character"/> property.</summary>
    public static readonly StyledProperty<string?> CharacterProperty =
        AvaloniaProperty.Register<HanziStrokePreview, string?>(nameof(Character));

    /// <summary>Identifies the <see cref="Analysis"/> property.</summary>
    public static readonly StyledProperty<AnalyzedCharacter?> AnalysisProperty =
        AvaloniaProperty.Register<HanziStrokePreview, AnalyzedCharacter?>(nameof(Analysis));

    /// <summary>Identifies the <see cref="PreviewBackground"/> property.</summary>
    public static readonly StyledProperty<IBrush?> PreviewBackgroundProperty =
        AvaloniaProperty.Register<HanziStrokePreview, IBrush?>(nameof(PreviewBackground), new SolidColorBrush(Colors.White));

    /// <summary>Identifies the <see cref="SkeletonBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> SkeletonBrushProperty =
        AvaloniaProperty.Register<HanziStrokePreview, IBrush?>(nameof(SkeletonBrush), new SolidColorBrush(Color.FromRgb(0xE0, 0x36, 0x36)));

    /// <summary>Identifies the <see cref="NodeBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> NodeBrushProperty =
        AvaloniaProperty.Register<HanziStrokePreview, IBrush?>(nameof(NodeBrush), new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)));

    /// <summary>Identifies the <see cref="GuideBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<HanziStrokePreview, IBrush?>(nameof(GuideBrush), new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)));

    /// <summary>Identifies the <see cref="ShowGuides"/> property.</summary>
    public static readonly StyledProperty<bool> ShowGuidesProperty =
        AvaloniaProperty.Register<HanziStrokePreview, bool>(nameof(ShowGuides), true);

    /// <summary>Identifies the <see cref="ShowNodes"/> property.</summary>
    public static readonly StyledProperty<bool> ShowNodesProperty =
        AvaloniaProperty.Register<HanziStrokePreview, bool>(nameof(ShowNodes), true);

    /// <summary>Identifies the <see cref="ShowStartMarkers"/> property.</summary>
    public static readonly StyledProperty<bool> ShowStartMarkersProperty =
        AvaloniaProperty.Register<HanziStrokePreview, bool>(nameof(ShowStartMarkers), true);

    /// <summary>Identifies the <see cref="LineThickness"/> property.</summary>
    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<HanziStrokePreview, double>(nameof(LineThickness), 2.5);

    /// <summary>Creates the preview.</summary>
    public HanziStrokePreview()
    {
        ClipToBounds = true;
    }

    /// <summary>The repository a character is looked up in.</summary>
    public HanziData? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>The character to preview (looked up in <see cref="Data"/>).</summary>
    public string? Character
    {
        get => GetValue(CharacterProperty);
        set => SetValue(CharacterProperty, value);
    }

    /// <summary>An analysis to preview instead of a repository character.</summary>
    public AnalyzedCharacter? Analysis
    {
        get => GetValue(AnalysisProperty);
        set => SetValue(AnalysisProperty, value);
    }

    /// <summary>Background of the preview square.</summary>
    public IBrush? PreviewBackground
    {
        get => GetValue(PreviewBackgroundProperty);
        set => SetValue(PreviewBackgroundProperty, value);
    }

    /// <summary>Brush of the reconstructed sub-strokes.</summary>
    public IBrush? SkeletonBrush
    {
        get => GetValue(SkeletonBrushProperty);
        set => SetValue(SkeletonBrushProperty, value);
    }

    /// <summary>Brush of the sub-stroke end points.</summary>
    public IBrush? NodeBrush
    {
        get => GetValue(NodeBrushProperty);
        set => SetValue(NodeBrushProperty, value);
    }

    /// <summary>Brush of the guide grid.</summary>
    public IBrush? GuideBrush
    {
        get => GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    /// <summary>Whether the guide grid is drawn.</summary>
    public bool ShowGuides
    {
        get => GetValue(ShowGuidesProperty);
        set => SetValue(ShowGuidesProperty, value);
    }

    /// <summary>Whether the end points of the sub-strokes are marked.</summary>
    public bool ShowNodes
    {
        get => GetValue(ShowNodesProperty);
        set => SetValue(ShowNodesProperty, value);
    }

    /// <summary>Whether the start of every stroke is marked with a filled dot (drawing order cue).</summary>
    public bool ShowStartMarkers
    {
        get => GetValue(ShowStartMarkersProperty);
        set => SetValue(ShowStartMarkersProperty, value);
    }

    /// <summary>Thickness of the skeleton lines.</summary>
    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    /// <summary>Shows a character of the repository.</summary>
    public void ShowCharacter(HanziData data, string character)
    {
        Data = data;
        Analysis = null;
        Character = character;
        InvalidateVisual();
    }

    /// <summary>Shows the analysis of a captured character.</summary>
    public void ShowAnalysis(AnalyzedCharacter analysis)
    {
        Analysis = analysis;
        Character = null;
        InvalidateVisual();
    }

    /// <summary>Clears the preview.</summary>
    public void Clear()
    {
        Analysis = null;
        Character = null;
        InvalidateVisual();
    }

    /// <summary>The segments that are currently drawn (also useful for tests).</summary>
    public IReadOnlyList<SkeletonSegment> GetSegments()
    {
        if (Analysis is { } analysis)
        {
            return StrokeSkeleton.FromAnalyzedCharacter(analysis);
        }

        if (Data is { } data && !string.IsNullOrEmpty(Character) && data.Find(Character) is { } entry)
        {
            return StrokeSkeleton.FromRepositoryCharacter(data, entry);
        }

        return Array.Empty<SkeletonSegment>();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0)
        {
            return;
        }

        var square = new Rect((Bounds.Width - side) / 2, (Bounds.Height - side) / 2, side, side);
        if (PreviewBackground is { } background)
        {
            context.DrawRectangle(background, null, square);
        }

        if (ShowGuides && GuideBrush is { } guideBrush)
        {
            StrokeAnalysisRenderer.DrawGrid(context, square, guideBrush);
        }

        var segments = GetSegments();
        StrokeAnalysisRenderer.DrawSkeleton(
            context,
            square,
            segments,
            SkeletonBrush,
            ShowNodes ? NodeBrush : null,
            LineThickness);

        if (ShowStartMarkers && SkeletonBrush is { } startBrush)
        {
            // A filled circle marks where each sub-stroke starts, i.e. the drawing order.
            foreach (var segment in segments)
            {
                var start = StrokeAnalysisRenderer.ToControlNormalized(segment.Start, square);
                context.DrawEllipse(startBrush, null, start, LineThickness, LineThickness);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        InvalidateVisual();
    }
}
