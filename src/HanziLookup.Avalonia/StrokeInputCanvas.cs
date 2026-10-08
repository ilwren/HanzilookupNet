using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using HanziLookup;

namespace HanziLookup.Avalonia;

/// <summary>
/// A square canvas that captures handwriting with mouse, pen or touch, shows the captured strokes,
/// can replay them as an animation ("simulated" drawing) and can overlay the analysis of the
/// recognizer (bounding box, reconstructed sub-stroke skeleton and pivot points).
/// </summary>
/// <remarks>
/// <para>
/// The control renders a square drawing area centred in its bounds; captured points are scaled into
/// the recognizer's coordinate space (<see cref="CoordinateSpace"/>, 256 x 256 by default - the same
/// space the shipped character data uses), so a stroke means the same thing whatever size the
/// control is rendered at.
/// </para>
/// <para>
/// Captured strokes are handed to <see cref="Session"/> (a <see cref="HandwritingSession"/>) when the
/// pointer is released, which analyses them and runs recognition. While the pointer is down the
/// in-progress stroke is only kept by the control.
/// </para>
/// </remarks>
public sealed class StrokeInputCanvas : Control
{
    /// <summary>Identifies the <see cref="CoordinateSpace"/> property.</summary>
    public static readonly StyledProperty<double> CoordinateSpaceProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, double>(nameof(CoordinateSpace), 256.0);

    /// <summary>Identifies the <see cref="Session"/> property.</summary>
    public static readonly StyledProperty<HandwritingSession?> SessionProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, HandwritingSession?>(nameof(Session));

    /// <summary>Identifies the <see cref="StrokeBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> StrokeBrushProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, IBrush?>(nameof(StrokeBrush), new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)));

    /// <summary>Identifies the <see cref="StrokeThickness"/> property.</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, double>(nameof(StrokeThickness), 4.0);

    /// <summary>Identifies the <see cref="CanvasBackground"/> property.</summary>
    public static readonly StyledProperty<IBrush?> CanvasBackgroundProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, IBrush?>(nameof(CanvasBackground), new SolidColorBrush(Colors.White));

    /// <summary>Identifies the <see cref="GuideBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> GuideBrushProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, IBrush?>(nameof(GuideBrush), new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)));

    /// <summary>Identifies the <see cref="SkeletonBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> SkeletonBrushProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, IBrush?>(nameof(SkeletonBrush), new SolidColorBrush(Color.FromRgb(0xE0, 0x36, 0x36)));

    /// <summary>Identifies the <see cref="PivotBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> PivotBrushProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, IBrush?>(nameof(PivotBrush), new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)));

    /// <summary>Identifies the <see cref="BoundingBoxBrush"/> property.</summary>
    public static readonly StyledProperty<IBrush?> BoundingBoxBrushProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, IBrush?>(nameof(BoundingBoxBrush), new SolidColorBrush(Color.FromArgb(0x80, 0x15, 0x65, 0xC0)));

    /// <summary>Identifies the <see cref="ShowGuides"/> property.</summary>
    public static readonly StyledProperty<bool> ShowGuidesProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, bool>(nameof(ShowGuides), true);

    /// <summary>Identifies the <see cref="ShowAnalysis"/> property.</summary>
    public static readonly StyledProperty<bool> ShowAnalysisProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, bool>(nameof(ShowAnalysis), true);

    /// <summary>Identifies the <see cref="IsInputEnabled"/> property.</summary>
    public static readonly StyledProperty<bool> IsInputEnabledProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, bool>(nameof(IsInputEnabled), true);

    /// <summary>Identifies the <see cref="PlaybackPointsPerSecond"/> property.</summary>
    public static readonly StyledProperty<double> PlaybackPointsPerSecondProperty =
        AvaloniaProperty.Register<StrokeInputCanvas, double>(nameof(PlaybackPointsPerSecond), 450.0);

    private readonly DispatcherTimer _playbackTimer;
    private RawStroke? _currentStroke;
    private bool _isPlaying;
    private int _playbackStrokeIndex;
    private int _playbackPointIndex;

    /// <summary>Creates the canvas.</summary>
    public StrokeInputCanvas()
    {
        ClipToBounds = true;
        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _playbackTimer.Tick += OnPlaybackTick;
    }

    /// <summary>Raised when a stroke was finished (either by the user or programmatically).</summary>
    public event EventHandler<RawStroke>? StrokeCompleted;

    /// <summary>Raised when a stroke animation finished.</summary>
    public event EventHandler? PlaybackCompleted;

    /// <summary>Raised when the in-progress stroke changed (useful to refresh a live preview).</summary>
    public event EventHandler? CurrentStrokeChanged;

    /// <summary>The coordinate space captured points are recorded in (256 x 256 by default).</summary>
    public double CoordinateSpace
    {
        get => GetValue(CoordinateSpaceProperty);
        set => SetValue(CoordinateSpaceProperty, value);
    }

    /// <summary>The session that receives the captured strokes and produces recognition results.</summary>
    public HandwritingSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>Brush used for captured strokes.</summary>
    public IBrush? StrokeBrush
    {
        get => GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    /// <summary>Thickness of captured strokes, in device independent pixels.</summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>Background of the writing square.</summary>
    public IBrush? CanvasBackground
    {
        get => GetValue(CanvasBackgroundProperty);
        set => SetValue(CanvasBackgroundProperty, value);
    }

    /// <summary>Brush of the 米字格 guide grid.</summary>
    public IBrush? GuideBrush
    {
        get => GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    /// <summary>Brush of the reconstructed sub-stroke skeleton.</summary>
    public IBrush? SkeletonBrush
    {
        get => GetValue(SkeletonBrushProperty);
        set => SetValue(SkeletonBrushProperty, value);
    }

    /// <summary>Brush of the pivot point markers.</summary>
    public IBrush? PivotBrush
    {
        get => GetValue(PivotBrushProperty);
        set => SetValue(PivotBrushProperty, value);
    }

    /// <summary>Brush of the bounding box of the analysis.</summary>
    public IBrush? BoundingBoxBrush
    {
        get => GetValue(BoundingBoxBrushProperty);
        set => SetValue(BoundingBoxBrushProperty, value);
    }

    /// <summary>Whether the guide grid is drawn.</summary>
    public bool ShowGuides
    {
        get => GetValue(ShowGuidesProperty);
        set => SetValue(ShowGuidesProperty, value);
    }

    /// <summary>Whether the analysis (bounding box, skeleton, pivots) is drawn over the strokes.</summary>
    public bool ShowAnalysis
    {
        get => GetValue(ShowAnalysisProperty);
        set => SetValue(ShowAnalysisProperty, value);
    }

    /// <summary>Whether pointer input is captured.</summary>
    public bool IsInputEnabled
    {
        get => GetValue(IsInputEnabledProperty);
        set => SetValue(IsInputEnabledProperty, value);
    }

    /// <summary>How many captured points the replay animation draws per second.</summary>
    public double PlaybackPointsPerSecond
    {
        get => GetValue(PlaybackPointsPerSecondProperty);
        set => SetValue(PlaybackPointsPerSecondProperty, value);
    }

    /// <summary>The stroke currently being drawn, if any.</summary>
    public RawStroke? CurrentStroke => _currentStroke;

    /// <summary>True while the replay animation is running.</summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>Removes all strokes (and results) from <see cref="Session"/>.</summary>
    public void Clear()
    {
        _currentStroke = null;
        Session?.Clear();
        InvalidateVisual();
    }

    /// <summary>Removes the last stroke from <see cref="Session"/>.</summary>
    public bool Undo()
    {
        _currentStroke = null;
        var removed = Session?.RemoveLastStroke() ?? false;
        InvalidateVisual();
        return removed;
    }

    /// <summary>Starts the stroke replay animation over the strokes of <see cref="Session"/>.</summary>
    public void StartPlayback()
    {
        if (Session is null || Session.Strokes.Count == 0)
        {
            return;
        }

        _isPlaying = true;
        _playbackStrokeIndex = 0;
        _playbackPointIndex = 0;
        _playbackTimer.Start();
        InvalidateVisual();
    }

    /// <summary>Stops the replay animation (drawing all strokes from then on).</summary>
    public void StopPlayback()
    {
        _isPlaying = false;
        _playbackTimer.Stop();
        InvalidateVisual();
    }

    /// <summary>Starts a stroke at a point of the recognizer's coordinate space (programmatic input).</summary>
    public void BeginStroke(StrokePoint point)
    {
        _currentStroke = new RawStroke(64);
        _currentStroke.Add(point);
        CurrentStrokeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Adds a point of the recognizer's coordinate space to the in-progress stroke.</summary>
    public void ExtendStroke(StrokePoint point)
    {
        if (_currentStroke is null)
        {
            return;
        }

        var last = _currentStroke[_currentStroke.Count - 1];
        if (last.DistanceTo(point) < 0.5)
        {
            return;
        }

        _currentStroke.Add(point);
        CurrentStrokeChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Finishes the in-progress stroke and hands it to <see cref="Session"/>.</summary>
    public void EndStroke()
    {
        var stroke = _currentStroke;
        _currentStroke = null;
        if (stroke is null)
        {
            return;
        }

        if (Session is not null)
        {
            Session.AddStroke(stroke);
        }

        StrokeCompleted?.Invoke(this, stroke);
        InvalidateVisual();
    }

    /// <summary>The square used for drawing, centred in the bounds of the control.</summary>
    public Rect GetDrawingSquare()
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0)
        {
            return default;
        }

        return new Rect((Bounds.Width - side) / 2, (Bounds.Height - side) / 2, side, side);
    }

    /// <summary>Converts a control point into the recognizer's coordinate space.</summary>
    public StrokePoint ToCoordinateSpace(Point point)
    {
        var square = GetDrawingSquare();
        if (square.Width <= 0 || CoordinateSpace <= 0)
        {
            return new StrokePoint(0, 0);
        }

        var scale = CoordinateSpace / square.Width;
        var x = Math.Clamp((point.X - square.X) * scale, 0, CoordinateSpace);
        var y = Math.Clamp((point.Y - square.Y) * scale, 0, CoordinateSpace);
        return new StrokePoint(x, y);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var square = GetDrawingSquare();
        if (square.Width <= 0)
        {
            return;
        }

        if (CanvasBackground is { } background)
        {
            context.DrawRectangle(background, null, square);
        }

        if (ShowGuides && GuideBrush is { } guideBrush)
        {
            StrokeAnalysisRenderer.DrawGrid(context, square, guideBrush);
        }

        var session = Session;
        var strokeBrush = StrokeBrush;
        var strokePen = strokeBrush is null
            ? null
            : new Pen(strokeBrush, StrokeThickness, null, PenLineCap.Round, PenLineJoin.Round);

        if (session is not null && !_isPlaying && strokePen is not null)
        {
            StrokeAnalysisRenderer.DrawStrokes(context, square, session.Strokes, strokeBrush!, StrokeThickness, CoordinateSpace);
        }

        if (session is not null && _isPlaying)
        {
            DrawPlaybackState(context, square, session);
        }

        if (_currentStroke is not null && strokePen is not null)
        {
            StrokeAnalysisRenderer.DrawStroke(context, square, _currentStroke, strokePen, CoordinateSpace);
        }

        if (ShowAnalysis && session?.Analysis is { } analysis)
        {
            StrokeAnalysisRenderer.DrawAnalysis(
                context,
                square,
                analysis,
                SkeletonBrush ?? Brushes.Red,
                PivotBrush ?? Brushes.Blue,
                BoundingBoxBrush ?? Brushes.SteelBlue,
                CoordinateSpace);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SessionProperty)
        {
            if (change.GetOldValue<HandwritingSession?>() is { } oldSession)
            {
                oldSession.Changed -= OnSessionChanged;
            }

            if (change.GetNewValue<HandwritingSession?>() is { } newSession)
            {
                newSession.Changed += OnSessionChanged;
            }

            InvalidateVisual();
        }
        else if (change.Property == StrokeBrushProperty ||
                 change.Property == StrokeThicknessProperty ||
                 change.Property == CanvasBackgroundProperty ||
                 change.Property == GuideBrushProperty ||
                 change.Property == SkeletonBrushProperty ||
                 change.Property == PivotBrushProperty ||
                 change.Property == BoundingBoxBrushProperty ||
                 change.Property == ShowGuidesProperty ||
                 change.Property == ShowAnalysisProperty ||
                 change.Property == CoordinateSpaceProperty)
        {
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!IsInputEnabled || Session is null || _isPlaying)
        {
            return;
        }

        var properties = e.GetCurrentPoint(this).Properties;
        if (e.Pointer.Type == PointerType.Mouse && !properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Pointer.Capture(this);
        BeginStroke(ToCoordinateSpace(e.GetPosition(this)));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_currentStroke is null || !IsInputEnabled)
        {
            return;
        }

        ExtendStroke(ToCoordinateSpace(e.GetPosition(this)));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_currentStroke is null)
        {
            return;
        }

        ExtendStroke(ToCoordinateSpace(e.GetPosition(this)));
        e.Pointer.Capture(null);
        EndStroke();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (_currentStroke is not null)
        {
            EndStroke();
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void DrawPlaybackState(DrawingContext context, Rect square, HandwritingSession session)
    {
        if (StrokeBrush is not { } brush)
        {
            return;
        }

        var pen = new Pen(brush, StrokeThickness, null, PenLineCap.Round, PenLineJoin.Round);
        for (var i = 0; i < session.Strokes.Count; ++i)
        {
            var stroke = session.Strokes[i];
            if (i < _playbackStrokeIndex)
            {
                StrokeAnalysisRenderer.DrawStroke(context, square, stroke, pen, CoordinateSpace);
            }
            else if (i == _playbackStrokeIndex)
            {
                StrokeAnalysisRenderer.DrawStroke(context, square, stroke, pen, CoordinateSpace, _playbackPointIndex);
            }
        }
    }

    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        var session = Session;
        if (!_isPlaying || session is null)
        {
            StopPlayback();
            return;
        }

        var pointsPerSecond = Math.Max(1.0, PlaybackPointsPerSecond);
        var remaining = Math.Max(1, (int)Math.Round(pointsPerSecond / 60.0));
        while (remaining > 0 && _playbackStrokeIndex < session.Strokes.Count)
        {
            var stroke = session.Strokes[_playbackStrokeIndex];
            var left = stroke.Count - _playbackPointIndex;
            if (left <= remaining)
            {
                remaining -= left;
                ++_playbackStrokeIndex;
                _playbackPointIndex = 0;
            }
            else
            {
                _playbackPointIndex += remaining;
                remaining = 0;
            }
        }

        if (_playbackStrokeIndex >= session.Strokes.Count)
        {
            _isPlaying = false;
            _playbackTimer.Stop();
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }
}
