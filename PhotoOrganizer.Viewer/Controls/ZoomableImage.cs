using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;

namespace PhotoOrganizer.Viewer.Controls;

public class ZoomableImage : Control
{
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<ZoomableImage, IImage?>(nameof(Source));

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private double _scale = 1.0;
    private Vector _translate;
    private Point _lastPointer;
    private bool _isPanning;

    private const double MinScale = 0.1;
    private const double MaxScale = 20.0;
    private const double ZoomStep = 1.15;

    static ZoomableImage()
    {
        AffectsRender<ZoomableImage>(SourceProperty);
    }

    public ZoomableImage()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var pos = e.GetPosition(this);

        double factor = e.Delta.Y > 0 ? ZoomStep : 1.0 / ZoomStep;
        double newScale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - _scale) < 1e-9) return;

        // Масштабирование относительно позиции курсора
        var rel = new Vector(pos.X - Bounds.Width / 2,
                             pos.Y - Bounds.Height / 2);
        _translate = (_translate - rel) * (newScale / _scale) + rel;
        _scale = newScale;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed)
        {
            _lastPointer = e.GetPosition(this);
            _isPanning = true;
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isPanning)
        {
            var pos = e.GetPosition(this);
            _translate += (Vector)(pos - _lastPointer);
            _lastPointer = pos;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isPanning)
        {
            _isPanning = false;
            Cursor = Cursor.Default;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isPanning = false;
        Cursor = Cursor.Default;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Source is null) return;

        var size = Source.Size;
        var destRect = new Rect(
            -size.Width / 2,
            -size.Height / 2,
            size.Width,
            size.Height);

        using (context.PushTransform(
            Matrix.CreateTranslation(_translate.X, _translate.Y)
            * Matrix.CreateScale(_scale, _scale)
            * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2)))
        {
            context.DrawImage(Source, destRect);
        }
    }
}