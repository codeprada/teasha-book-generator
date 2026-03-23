using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TeashaBookGenerator.Models;
using TeashaBookGenerator.ViewModels;

namespace TeashaBookGenerator.Views;

public partial class MainWindow : Window
{
    private const double ResizeHandleSize = 8;
    private const double MinRegionSize = 20;

    private Canvas? _canvas;
    private Image? _displayImage;
    private Border? _selectionBox;

    private readonly Dictionary<OverlayRegion, OverlayVisual> _overlayVisuals = new();

    // Interaction state
    private enum InteractionMode { None, Drawing, Moving, Resizing }
    private InteractionMode _interaction;
    private Point _dragStart;
    private double _origX, _origY, _origW, _origH;
    private ResizeEdge _resizeEdge;

    [Flags]
    private enum ResizeEdge
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8,
        TopLeft = Top | Left,
        TopRight = Top | Right,
        BottomLeft = Bottom | Left,
        BottomRight = Bottom | Right,
    }

    private class OverlayVisual
    {
        public Border Box { get; init; } = null!;
        public Control Content { get; init; } = null!;
    }

    public MainWindow()
    {
        InitializeComponent();
        _canvas = this.FindControl<Canvas>("ImageCanvas");
        _displayImage = this.FindControl<Image>("DisplayImage");
        _selectionBox = this.FindControl<Border>("SelectionBox");
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel != null)
        {
            ViewModel.PreviewInvalidated += RefreshAllOverlayVisuals;
            ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.SelectedOverlay))
                    RefreshAllOverlayVisuals();
            };
            ViewModel.RequestLayoutThenRestore += () =>
            {
                ClearAllOverlayVisuals();
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    UpdateImageLayout();
                    ViewModel.RestorePendingOverlays();
                    RefreshAllOverlayVisuals();
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            };
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateImageLayout();
        RefreshAllOverlayVisuals();
    }

    #region Image layout

    private void UpdateImageLayout()
    {
        if (_canvas == null || _displayImage == null || ViewModel?.ImageSource == null)
            return;

        var canvasBounds = _canvas.Bounds;
        if (canvasBounds.Width <= 0 || canvasBounds.Height <= 0) return;

        var imgWidth = ViewModel.OriginalImageWidth;
        var imgHeight = ViewModel.OriginalImageHeight;
        if (imgWidth <= 0 || imgHeight <= 0) return;

        double scale = Math.Min(canvasBounds.Width / imgWidth, canvasBounds.Height / imgHeight);
        double displayWidth = imgWidth * scale;
        double displayHeight = imgHeight * scale;

        _displayImage.Width = displayWidth;
        _displayImage.Height = displayHeight;

        Canvas.SetLeft(_displayImage, (canvasBounds.Width - displayWidth) / 2);
        Canvas.SetTop(_displayImage, (canvasBounds.Height - displayHeight) / 2);

        ViewModel.ImageDisplayWidth = displayWidth;
        ViewModel.ImageDisplayHeight = displayHeight;
    }

    private (double offsetX, double offsetY) GetImageOffset()
    {
        if (_displayImage == null) return (0, 0);
        double ox = Canvas.GetLeft(_displayImage);
        double oy = Canvas.GetTop(_displayImage);
        return (double.IsNaN(ox) ? 0 : ox, double.IsNaN(oy) ? 0 : oy);
    }

    /// <summary>Clamp a canvas point to the image display bounds.</summary>
    private Point ClampToImage(Point p)
    {
        var (ox, oy) = GetImageOffset();
        double w = ViewModel?.ImageDisplayWidth ?? 0;
        double h = ViewModel?.ImageDisplayHeight ?? 0;
        return new Point(Math.Clamp(p.X, ox, ox + w), Math.Clamp(p.Y, oy, oy + h));
    }

    #endregion

    #region Toolbar clicks

    private async void OnLoadImageClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image Files")
                    { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.tiff", "*.webp"] },
                new FilePickerFileType("All Files") { Patterns = ["*.*"] }
            ]
        });

        if (files.Count > 0)
        {
            ClearAllOverlayVisuals();
            ViewModel?.LoadImageFromPath(files[0].Path.LocalPath);
            Avalonia.Threading.Dispatcher.UIThread.Post(UpdateImageLayout,
                Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private async void OnBrowseOverlayImageClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedImageOverlay is not { } imgOverlay) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Overlay Image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image Files")
                    { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.tiff", "*.webp"] },
                new FilePickerFileType("All Files") { Patterns = ["*.*"] }
            ]
        });

        if (files.Count > 0)
        {
            imgOverlay.LoadImage(files[0].Path.LocalPath);
        }
    }

    private void OnColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color } && ViewModel?.SelectedTextOverlay is { } overlay)
            overlay.FontColor = color;
    }

    #endregion

    #region Pointer interaction (draw / move / resize)

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel == null || !ViewModel.IsImageLoaded || _canvas == null) return;

        var pos = e.GetPosition(_canvas);
        var (ox, oy) = GetImageOffset();
        double relX = pos.X - ox;
        double relY = pos.Y - oy;

        // ── Drawing mode: start a new region ──
        if (ViewModel.IsDrawingMode)
        {
            pos = ClampToImage(pos);
            _dragStart = pos;
            _interaction = InteractionMode.Drawing;

            if (_selectionBox != null)
            {
                _selectionBox.IsVisible = true;
                _selectionBox.Width = 0;
                _selectionBox.Height = 0;
                Canvas.SetLeft(_selectionBox, pos.X);
                Canvas.SetTop(_selectionBox, pos.Y);
            }
            e.Handled = true;
            return;
        }

        // ── Check resize handles on the selected overlay first ──
        if (ViewModel.SelectedOverlay is { } sel)
        {
            var edge = HitTestResize(sel, relX, relY);
            if (edge != ResizeEdge.None)
            {
                _interaction = InteractionMode.Resizing;
                _resizeEdge = edge;
                _dragStart = pos;
                _origX = sel.X; _origY = sel.Y;
                _origW = sel.Width; _origH = sel.Height;
                e.Handled = true;
                return;
            }
        }

        // ── Hit-test overlays for move or selection ──
        for (int i = ViewModel.Overlays.Count - 1; i >= 0; i--)
        {
            var ov = ViewModel.Overlays[i];
            if (relX >= ov.X && relX <= ov.X + ov.Width &&
                relY >= ov.Y && relY <= ov.Y + ov.Height)
            {
                ViewModel.SelectedOverlay = ov;
                _interaction = InteractionMode.Moving;
                _dragStart = pos;
                _origX = ov.X; _origY = ov.Y;
                _origW = ov.Width; _origH = ov.Height;
                e.Handled = true;
                return;
            }
        }

        // Clicked on empty space — deselect
        ViewModel.SelectedOverlay = null;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_canvas == null || ViewModel == null) return;

        var pos = e.GetPosition(_canvas);

        // Update cursor based on hover
        if (_interaction == InteractionMode.None && !ViewModel.IsDrawingMode)
        {
            UpdateCursor(pos);
        }

        if (_interaction == InteractionMode.Drawing)
        {
            pos = ClampToImage(pos);
            if (_selectionBox != null)
            {
                double left = Math.Min(_dragStart.X, pos.X);
                double top = Math.Min(_dragStart.Y, pos.Y);
                Canvas.SetLeft(_selectionBox, left);
                Canvas.SetTop(_selectionBox, top);
                _selectionBox.Width = Math.Abs(pos.X - _dragStart.X);
                _selectionBox.Height = Math.Abs(pos.Y - _dragStart.Y);
            }
            e.Handled = true;
        }
        else if (_interaction == InteractionMode.Moving && ViewModel.SelectedOverlay is { } mov)
        {
            var (ox, oy) = GetImageOffset();
            double dx = pos.X - _dragStart.X;
            double dy = pos.Y - _dragStart.Y;

            double newX = Math.Clamp(_origX + dx, 0, ViewModel.ImageDisplayWidth - mov.Width);
            double newY = Math.Clamp(_origY + dy, 0, ViewModel.ImageDisplayHeight - mov.Height);
            mov.X = newX;
            mov.Y = newY;
            e.Handled = true;
        }
        else if (_interaction == InteractionMode.Resizing && ViewModel.SelectedOverlay is { } rsz)
        {
            double dx = pos.X - _dragStart.X;
            double dy = pos.Y - _dragStart.Y;

            double newX = _origX, newY = _origY, newW = _origW, newH = _origH;

            if (_resizeEdge.HasFlag(ResizeEdge.Right))
                newW = Math.Max(MinRegionSize, _origW + dx);
            if (_resizeEdge.HasFlag(ResizeEdge.Bottom))
                newH = Math.Max(MinRegionSize, _origH + dy);
            if (_resizeEdge.HasFlag(ResizeEdge.Left))
            {
                double maxDx = _origW - MinRegionSize;
                double clampedDx = Math.Min(dx, maxDx);
                newX = _origX + clampedDx;
                newW = _origW - clampedDx;
            }
            if (_resizeEdge.HasFlag(ResizeEdge.Top))
            {
                double maxDy = _origH - MinRegionSize;
                double clampedDy = Math.Min(dy, maxDy);
                newY = _origY + clampedDy;
                newH = _origH - clampedDy;
            }

            // Clamp to image bounds
            newX = Math.Max(0, newX);
            newY = Math.Max(0, newY);
            newW = Math.Min(newW, ViewModel.ImageDisplayWidth - newX);
            newH = Math.Min(newH, ViewModel.ImageDisplayHeight - newY);

            rsz.X = newX; rsz.Y = newY;
            rsz.Width = newW; rsz.Height = newH;
            e.Handled = true;
        }
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_interaction == InteractionMode.Drawing && ViewModel != null)
        {
            if (_selectionBox != null)
                _selectionBox.IsVisible = false;

            var (ox, oy) = GetImageOffset();
            double left = Canvas.GetLeft(_selectionBox!);
            double top = Canvas.GetTop(_selectionBox!);
            double w = _selectionBox!.Width;
            double h = _selectionBox.Height;

            if (w > 5 && h > 5)
                ViewModel.CommitNewOverlay(left - ox, top - oy, w, h);
            else
            {
                ViewModel.DrawingMode = ViewModels.DrawingMode.None;
                ViewModel.StatusMessage = "Box too small, cancelled.";
            }
        }

        _interaction = InteractionMode.None;
        e.Handled = true;
    }

    private ResizeEdge HitTestResize(OverlayRegion overlay, double relX, double relY)
    {
        double l = overlay.X, t = overlay.Y;
        double r = l + overlay.Width, b = t + overlay.Height;

        bool nearL = Math.Abs(relX - l) <= ResizeHandleSize && relY >= t - ResizeHandleSize && relY <= b + ResizeHandleSize;
        bool nearR = Math.Abs(relX - r) <= ResizeHandleSize && relY >= t - ResizeHandleSize && relY <= b + ResizeHandleSize;
        bool nearT = Math.Abs(relY - t) <= ResizeHandleSize && relX >= l - ResizeHandleSize && relX <= r + ResizeHandleSize;
        bool nearB = Math.Abs(relY - b) <= ResizeHandleSize && relX >= l - ResizeHandleSize && relX <= r + ResizeHandleSize;

        var edge = ResizeEdge.None;
        if (nearL) edge |= ResizeEdge.Left;
        if (nearR) edge |= ResizeEdge.Right;
        if (nearT) edge |= ResizeEdge.Top;
        if (nearB) edge |= ResizeEdge.Bottom;
        return edge;
    }

    private void UpdateCursor(Point canvasPos)
    {
        if (ViewModel?.SelectedOverlay == null) { Cursor = Cursor.Default; return; }

        var (ox, oy) = GetImageOffset();
        var edge = HitTestResize(ViewModel.SelectedOverlay, canvasPos.X - ox, canvasPos.Y - oy);

        Cursor = edge switch
        {
            ResizeEdge.Left or ResizeEdge.Right => new Cursor(StandardCursorType.SizeWestEast),
            ResizeEdge.Top or ResizeEdge.Bottom => new Cursor(StandardCursorType.SizeNorthSouth),
            ResizeEdge.TopLeft or ResizeEdge.BottomRight => new Cursor(StandardCursorType.TopLeftCorner),
            ResizeEdge.TopRight or ResizeEdge.BottomLeft => new Cursor(StandardCursorType.TopRightCorner),
            _ => IsOverAnyOverlay(canvasPos.X - ox, canvasPos.Y - oy)
                ? new Cursor(StandardCursorType.SizeAll)
                : Cursor.Default
        };
    }

    private bool IsOverAnyOverlay(double relX, double relY)
    {
        return ViewModel?.Overlays.Any(ov =>
            relX >= ov.X && relX <= ov.X + ov.Width &&
            relY >= ov.Y && relY <= ov.Y + ov.Height) ?? false;
    }

    #endregion

    #region Live preview rendering

    private void RefreshAllOverlayVisuals()
    {
        if (_canvas == null || ViewModel == null) return;

        // Remove stale visuals
        var toRemove = new List<OverlayRegion>();
        foreach (var kv in _overlayVisuals)
        {
            if (!ViewModel.Overlays.Contains(kv.Key))
            {
                _canvas.Children.Remove(kv.Value.Box);
                _canvas.Children.Remove(kv.Value.Content);
                toRemove.Add(kv.Key);
            }
        }
        foreach (var key in toRemove)
            _overlayVisuals.Remove(key);

        var (offsetX, offsetY) = GetImageOffset();

        foreach (var overlay in ViewModel.Overlays)
        {
            if (!_overlayVisuals.TryGetValue(overlay, out var visual))
            {
                visual = CreateVisualForOverlay(overlay);
                _overlayVisuals[overlay] = visual;
            }

            // Position the box
            Canvas.SetLeft(visual.Box, overlay.X + offsetX);
            Canvas.SetTop(visual.Box, overlay.Y + offsetY);
            visual.Box.Width = Math.Max(0, overlay.Width);
            visual.Box.Height = Math.Max(0, overlay.Height);

            bool isSelected = overlay == ViewModel.SelectedOverlay;
            visual.Box.BorderBrush = isSelected
                ? new SolidColorBrush(Color.Parse("#2196F3"))
                : new SolidColorBrush(Color.Parse("#888888"));
            visual.Box.Background = isSelected
                ? new SolidColorBrush(Color.Parse("#222196F3"))
                : new SolidColorBrush(Color.Parse("#11888888"));

            // Position the content
            Canvas.SetLeft(visual.Content, overlay.X + offsetX);
            Canvas.SetTop(visual.Content, overlay.Y + offsetY);

            // Update content based on type
            if (overlay is TextOverlayRegion textOv && visual.Content is TextBlock tb)
            {
                tb.Width = Math.Max(0, overlay.Width);
                tb.Height = Math.Max(0, overlay.Height);
                tb.Padding = new Thickness(3, 2);
                tb.Text = textOv.Text;

                // Match the Generate scaling: Generate uses fontSize * Max(scaleX, scaleY)
                // where scale = Original / Display. Preview needs the inverse.
                double displayScale = (ViewModel.OriginalImageWidth > 0 && ViewModel.ImageDisplayWidth > 0)
                    ? Math.Min(ViewModel.ImageDisplayWidth / ViewModel.OriginalImageWidth,
                               ViewModel.ImageDisplayHeight / ViewModel.OriginalImageHeight)
                    : 1.0;
                tb.FontSize = Math.Max(6, textOv.FontSize * displayScale);

                try { tb.Foreground = new SolidColorBrush(Color.Parse(textOv.FontColor)); }
                catch { tb.Foreground = Brushes.Black; }

                if (textOv.SelectedFont != null)
                    tb.FontFamily = textOv.SelectedFont.FontFamily;

                tb.TextAlignment = textOv.Alignment switch
                {
                    "Center" => TextAlignment.Center,
                    "Right" => TextAlignment.Right,
                    _ => TextAlignment.Left
                };
            }
            else if (overlay is ImageOverlayRegion imgOv && visual.Content is Image img)
            {
                img.Width = Math.Max(0, overlay.Width);
                img.Height = Math.Max(0, overlay.Height);
                img.Source = imgOv.PreviewSource;
                img.Opacity = imgOv.Opacity / 100.0;
            }

        }

        // Rebuild resize handles for the selected overlay
        ClearResizeHandles();
        EnsureResizeHandles();
    }

    private OverlayVisual CreateVisualForOverlay(OverlayRegion overlay)
    {
        var box = new Border
        {
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false
        };

        Control content;
        if (overlay is ImageOverlayRegion)
        {
            content = new Image
            {
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
        }
        else
        {
            content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                IsHitTestVisible = false
            };
        }

        _canvas!.Children.Add(box);
        _canvas.Children.Add(content);

        return new OverlayVisual { Box = box, Content = content };
    }

    // Resize handle visuals keyed by position name
    private readonly Dictionary<string, Border> _resizeHandles = new();

    private void EnsureResizeHandles()
    {
        if (_canvas == null || ViewModel?.SelectedOverlay == null) return;

        // Remove old handles
        foreach (var h in _resizeHandles.Values)
            _canvas.Children.Remove(h);
        _resizeHandles.Clear();

        var sel = ViewModel.SelectedOverlay;
        var (ox, oy) = GetImageOffset();
        double hs = ResizeHandleSize;

        var positions = new (string name, double cx, double cy)[]
        {
            ("TL", sel.X, sel.Y),
            ("T",  sel.X + sel.Width / 2, sel.Y),
            ("TR", sel.X + sel.Width, sel.Y),
            ("L",  sel.X, sel.Y + sel.Height / 2),
            ("R",  sel.X + sel.Width, sel.Y + sel.Height / 2),
            ("BL", sel.X, sel.Y + sel.Height),
            ("B",  sel.X + sel.Width / 2, sel.Y + sel.Height),
            ("BR", sel.X + sel.Width, sel.Y + sel.Height),
        };

        foreach (var (name, cx, cy) in positions)
        {
            var handle = new Border
            {
                Width = hs,
                Height = hs,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.Parse("#2196F3")),
                BorderThickness = new Thickness(1.5),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(handle, cx + ox - hs / 2);
            Canvas.SetTop(handle, cy + oy - hs / 2);
            _canvas.Children.Add(handle);
            _resizeHandles[name] = handle;
        }
    }

    private void ClearResizeHandles()
    {
        if (_canvas == null) return;
        foreach (var h in _resizeHandles.Values)
            _canvas.Children.Remove(h);
        _resizeHandles.Clear();
    }

    private void ClearAllOverlayVisuals()
    {
        if (_canvas == null) return;
        foreach (var kv in _overlayVisuals)
        {
            _canvas.Children.Remove(kv.Value.Box);
            _canvas.Children.Remove(kv.Value.Content);
        }
        _overlayVisuals.Clear();
        ClearResizeHandles();
    }

    #endregion
}
