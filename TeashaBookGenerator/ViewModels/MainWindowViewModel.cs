using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;
using TeashaBookGenerator.Models;

namespace TeashaBookGenerator.ViewModels;

public enum DrawingMode
{
    None,
    DrawText,
    DrawImage
}

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly string FontCacheDir = Path.Combine(Path.GetTempPath(), "TeashaBookGenerator_Fonts");

    [ObservableProperty]
    private Bitmap? _imageSource;

    [ObservableProperty]
    private string _imagePath = string.Empty;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private double _imageDisplayWidth;

    [ObservableProperty]
    private double _imageDisplayHeight;

    [ObservableProperty]
    private int _originalImageWidth;

    [ObservableProperty]
    private int _originalImageHeight;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isImageLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTextOverlay))]
    [NotifyPropertyChangedFor(nameof(SelectedImageOverlay))]
    [NotifyPropertyChangedFor(nameof(IsTextOverlaySelected))]
    [NotifyPropertyChangedFor(nameof(IsImageOverlaySelected))]
    private OverlayRegion? _selectedOverlay;

    [ObservableProperty]
    private DrawingMode _drawingMode;

    public bool IsDrawingMode => DrawingMode != DrawingMode.None;

    public TextOverlayRegion? SelectedTextOverlay => SelectedOverlay as TextOverlayRegion;
    public ImageOverlayRegion? SelectedImageOverlay => SelectedOverlay as ImageOverlayRegion;
    public bool IsTextOverlaySelected => SelectedOverlay is TextOverlayRegion;
    public bool IsImageOverlaySelected => SelectedOverlay is ImageOverlayRegion;

    public ObservableCollection<OverlayRegion> Overlays { get; } = [];

    public ObservableCollection<EmbeddedFont> AvailableFonts { get; } = [];

    public ObservableCollection<string> AlignmentOptions { get; } = ["Left", "Center", "Right"];

    public ObservableCollection<int> FontSizes { get; } =
        [8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 48, 56, 64, 72, 96, 128];

    public event Action? PreviewInvalidated;

    private readonly Dictionary<string, string> _fontFileCache = new();
    private int _textCount;
    private int _imageCount;

    public MainWindowViewModel()
    {
        Overlays.CollectionChanged += OnOverlaysCollectionChanged;
        InitializeFonts();
    }

    private void InitializeFonts()
    {
        var fonts = new (string displayName, string fileName, string familyName)[]
        {
            ("Open Sans", "OpenSans-Regular.ttf", "Open Sans"),
            ("Roboto", "Roboto-Regular.ttf", "Roboto"),
            ("Lato", "Lato-Regular.ttf", "Lato"),
            ("Merriweather", "Merriweather-Regular.ttf", "Merriweather"),
            ("Playfair Display", "PlayfairDisplay-Regular.ttf", "Playfair Display"),
            ("Montserrat", "Montserrat-Regular.ttf", "Montserrat"),
            ("Oswald", "Oswald-Regular.ttf", "Oswald"),
            ("Raleway", "Raleway-Regular.ttf", "Raleway"),
            ("Source Sans 3", "SourceSans3-Regular.ttf", "Source Sans 3"),
            ("Poppins", "Poppins-Regular.ttf", "Poppins"),
            ("Nunito", "Nunito-Regular.ttf", "Nunito"),
            ("PT Serif", "PTSerif-Regular.ttf", "PT Serif"),
            ("Roboto Mono", "RobotoMono-Regular.ttf", "Roboto Mono"),
            ("Dancing Script", "DancingScript-Regular.ttf", "Dancing Script"),
            ("Pacifico", "Pacifico-Regular.ttf", "Pacifico"),
        };

        foreach (var (displayName, fileName, familyName) in fonts)
        {
            var resourcePath = $"avares://TeashaBookGenerator/Assets/Fonts/{fileName}";
            AvailableFonts.Add(new EmbeddedFont
            {
                DisplayName = displayName,
                FontFamily = new Avalonia.Media.FontFamily($"{resourcePath}#{familyName}"),
                ResourcePath = resourcePath,
            });
        }
    }

    private string GetFontFilePath(EmbeddedFont font)
    {
        if (_fontFileCache.TryGetValue(font.ResourcePath, out var cached) && File.Exists(cached))
            return cached;

        Directory.CreateDirectory(FontCacheDir);

        var uri = new Uri(font.ResourcePath);
        var assets = Avalonia.Platform.AssetLoader.Open(uri);
        var fileName = Path.GetFileName(uri.AbsolutePath);
        var destPath = Path.Combine(FontCacheDir, fileName);

        using (var fs = File.Create(destPath))
        {
            assets.CopyTo(fs);
        }

        _fontFileCache[font.ResourcePath] = destPath;
        return destPath;
    }

    private void OnOverlaysCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (OverlayRegion overlay in e.NewItems)
                overlay.PropertyChanged += OnOverlayPropertyChanged;
        if (e.OldItems != null)
            foreach (OverlayRegion overlay in e.OldItems)
                overlay.PropertyChanged -= OnOverlayPropertyChanged;
        PreviewInvalidated?.Invoke();
    }

    private void OnOverlayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        PreviewInvalidated?.Invoke();
    }

    public void RaisePreviewInvalidated() => PreviewInvalidated?.Invoke();

    public void LoadImageFromPath(string path)
    {
        try
        {
            ImagePath = path;
            var bitmap = new Bitmap(path);
            ImageSource = bitmap;
            OriginalImageWidth = bitmap.PixelSize.Width;
            OriginalImageHeight = bitmap.PixelSize.Height;
            IsImageLoaded = true;
            Overlays.Clear();
            SelectedOverlay = null;
            _textCount = 0;
            _imageCount = 0;

            var dir = Path.GetDirectoryName(path) ?? ".";
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            OutputPath = Path.Combine(dir, $"{name}_output{ext}");

            StatusMessage = $"Loaded: {Path.GetFileName(path)} ({OriginalImageWidth}x{OriginalImageHeight})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading image: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddTextOverlay()
    {
        DrawingMode = DrawingMode.DrawText;
        OnPropertyChanged(nameof(IsDrawingMode));
        StatusMessage = "Draw a box on the image to place the new text region.";
    }

    [RelayCommand]
    private void AddImageOverlay()
    {
        DrawingMode = DrawingMode.DrawImage;
        OnPropertyChanged(nameof(IsDrawingMode));
        StatusMessage = "Draw a box on the image to place the new image region.";
    }

    public void CommitNewOverlay(double x, double y, double width, double height)
    {
        OverlayRegion overlay;
        if (DrawingMode == DrawingMode.DrawImage)
        {
            _imageCount++;
            overlay = new ImageOverlayRegion
            {
                X = x, Y = y, Width = width, Height = height,
                Label = $"Image {_imageCount}"
            };
        }
        else
        {
            _textCount++;
            overlay = new TextOverlayRegion
            {
                X = x, Y = y, Width = width, Height = height,
                Label = $"Text {_textCount}",
                SelectedFont = AvailableFonts.Count > 0 ? AvailableFonts[0] : null
            };
        }

        Overlays.Add(overlay);
        SelectedOverlay = overlay;
        DrawingMode = DrawingMode.None;
        OnPropertyChanged(nameof(IsDrawingMode));
        StatusMessage = $"Added {overlay.Label}. Edit its properties in the panel.";
    }

    [RelayCommand]
    private void RemoveOverlay()
    {
        if (SelectedOverlay == null) return;
        var idx = Overlays.IndexOf(SelectedOverlay);
        Overlays.Remove(SelectedOverlay);
        SelectedOverlay = Overlays.Count > 0
            ? Overlays[Math.Min(idx, Overlays.Count - 1)]
            : null;
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var topLevel = App.TopLevel;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Output Image",
            SuggestedFileName = Path.GetFileName(OutputPath),
            FileTypeChoices =
            [
                new FilePickerFileType("PNG Image") { Patterns = ["*.png"] },
                new FilePickerFileType("JPEG Image") { Patterns = ["*.jpg", "*.jpeg"] },
                new FilePickerFileType("All Files") { Patterns = ["*.*"] }
            ]
        });

        if (file != null)
            OutputPath = file.Path.LocalPath;
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        var topLevel = App.TopLevel;
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Project",
            SuggestedFileName = Path.GetFileNameWithoutExtension(ImagePath) + ".tbook",
            FileTypeChoices =
            [
                new FilePickerFileType("TBook Project") { Patterns = ["*.tbook"] }
            ]
        });

        if (file == null) return;

        try
        {
            var projectPath = file.Path.LocalPath;
            var projectDir = Path.GetDirectoryName(projectPath) ?? ".";
            var project = new ProjectData
            {
                ImagePath = MakeRelativePath(projectDir, ImagePath),
                OutputPath = MakeRelativePath(projectDir, OutputPath),
            };

            foreach (var overlay in Overlays)
            {
                if (overlay is TextOverlayRegion textOv)
                {
                    project.Overlays.Add(new TextOverlayData
                    {
                        Label = textOv.Label,
                        X = textOv.X / ImageDisplayWidth,
                        Y = textOv.Y / ImageDisplayHeight,
                        Width = textOv.Width / ImageDisplayWidth,
                        Height = textOv.Height / ImageDisplayHeight,
                        Text = textOv.Text,
                        FontName = textOv.SelectedFont?.DisplayName ?? "",
                        FontSize = textOv.FontSize,
                        FontColor = textOv.FontColor,
                        Alignment = textOv.Alignment,
                    });
                }
                else if (overlay is ImageOverlayRegion imgOv)
                {
                    project.Overlays.Add(new ImageOverlayData
                    {
                        Label = imgOv.Label,
                        X = imgOv.X / ImageDisplayWidth,
                        Y = imgOv.Y / ImageDisplayHeight,
                        Width = imgOv.Width / ImageDisplayWidth,
                        Height = imgOv.Height / ImageDisplayHeight,
                        ImagePath = MakeRelativePath(projectDir, imgOv.ImagePath),
                        Opacity = imgOv.Opacity,
                    });
                }
            }

            var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(projectPath, json);
            StatusMessage = $"Project saved: {Path.GetFileName(projectPath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving project: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var topLevel = App.TopLevel;
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("TBook Project") { Patterns = ["*.tbook"] }
            ]
        });

        if (files.Count == 0) return;

        try
        {
            var projectPath = files[0].Path.LocalPath;
            var projectDir = Path.GetDirectoryName(projectPath) ?? ".";
            var json = await File.ReadAllTextAsync(projectPath);
            var project = JsonSerializer.Deserialize<ProjectData>(json);

            if (project == null)
            {
                StatusMessage = "Invalid project file.";
                return;
            }

            // Resolve paths
            var imgPath = ResolveRelativePath(projectDir, project.ImagePath);
            if (!File.Exists(imgPath))
            {
                StatusMessage = $"Image not found: {imgPath}";
                return;
            }

            // Load the background image (this clears overlays)
            LoadImageFromPath(imgPath);
            OutputPath = ResolveRelativePath(projectDir, project.OutputPath);

            // Need display dimensions to be set before restoring overlays.
            // Signal the view to layout, then restore overlays.
            _pendingProject = (project, projectDir);
            RequestLayoutThenRestore?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading project: {ex.Message}";
        }
    }

    private (ProjectData project, string projectDir)? _pendingProject;

    /// <summary>
    /// Raised when the view should perform layout and then call RestorePendingOverlays.
    /// </summary>
    public event Action? RequestLayoutThenRestore;

    public void RestorePendingOverlays()
    {
        if (_pendingProject is not { } pending) return;
        _pendingProject = null;

        var (project, projectDir) = pending;

        foreach (var data in project.Overlays)
        {
            if (data is TextOverlayData textData)
            {
                _textCount++;
                var overlay = new TextOverlayRegion
                {
                    Label = string.IsNullOrEmpty(textData.Label) ? $"Text {_textCount}" : textData.Label,
                    X = textData.X * ImageDisplayWidth,
                    Y = textData.Y * ImageDisplayHeight,
                    Width = textData.Width * ImageDisplayWidth,
                    Height = textData.Height * ImageDisplayHeight,
                    Text = textData.Text,
                    FontSize = textData.FontSize,
                    FontColor = textData.FontColor,
                    Alignment = textData.Alignment,
                    SelectedFont = AvailableFonts.FirstOrDefault(f => f.DisplayName == textData.FontName)
                                   ?? (AvailableFonts.Count > 0 ? AvailableFonts[0] : null),
                };
                Overlays.Add(overlay);
            }
            else if (data is ImageOverlayData imgData)
            {
                _imageCount++;
                var imgPath = ResolveRelativePath(projectDir, imgData.ImagePath);
                var overlay = new ImageOverlayRegion
                {
                    Label = string.IsNullOrEmpty(imgData.Label) ? $"Image {_imageCount}" : imgData.Label,
                    X = imgData.X * ImageDisplayWidth,
                    Y = imgData.Y * ImageDisplayHeight,
                    Width = imgData.Width * ImageDisplayWidth,
                    Height = imgData.Height * ImageDisplayHeight,
                    Opacity = imgData.Opacity,
                };

                if (File.Exists(imgPath))
                    overlay.LoadImage(imgPath);

                Overlays.Add(overlay);
            }
        }

        if (Overlays.Count > 0)
            SelectedOverlay = Overlays[0];

        StatusMessage = $"Project loaded with {Overlays.Count} overlay(s).";
    }

    private static string MakeRelativePath(string basePath, string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath)) return string.Empty;
        try
        {
            var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var targetUri = new Uri(targetPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(targetUri).ToString())
                       .Replace('/', Path.DirectorySeparatorChar);
        }
        catch
        {
            return targetPath;
        }
    }

    private static string ResolveRelativePath(string basePath, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return string.Empty;
        if (Path.IsPathRooted(relativePath)) return relativePath;
        return Path.GetFullPath(Path.Combine(basePath, relativePath));
    }

    [RelayCommand]
    private void Generate()
    {
        if (!IsImageLoaded) { StatusMessage = "No image loaded."; return; }
        if (Overlays.Count == 0) { StatusMessage = "Add at least one overlay."; return; }
        if (string.IsNullOrWhiteSpace(OutputPath)) { StatusMessage = "Specify an output path."; return; }

        try
        {
            double scaleX = OriginalImageWidth / ImageDisplayWidth;
            double scaleY = OriginalImageHeight / ImageDisplayHeight;

            using var image = new MagickImage(ImagePath);

            foreach (var overlay in Overlays)
            {
                if (overlay.Width < 1 || overlay.Height < 1) continue;

                int actualX = (int)(overlay.X * scaleX);
                int actualY = (int)(overlay.Y * scaleY);
                int actualWidth = (int)(overlay.Width * scaleX);
                int actualHeight = (int)(overlay.Height * scaleY);

                if (overlay is TextOverlayRegion textOverlay)
                {
                    if (string.IsNullOrWhiteSpace(textOverlay.Text)) continue;

                    string fontPath = textOverlay.SelectedFont != null
                        ? GetFontFilePath(textOverlay.SelectedFont)
                        : "DejaVu-Sans";

                    var settings = new MagickReadSettings
                    {
                        Font = fontPath,
                        FontPointsize = textOverlay.FontSize * Math.Max(scaleX, scaleY),
                        FillColor = new MagickColor(textOverlay.FontColor),
                        BackgroundColor = MagickColors.Transparent,
                        Width = (uint)actualWidth,
                        Height = (uint)actualHeight,
                        TextGravity = textOverlay.Alignment switch
                        {
                            "Center" => Gravity.North,
                            "Right" => Gravity.Northeast,
                            _ => Gravity.Northwest
                        }
                    };

                    using var textImage = new MagickImage($"caption:{textOverlay.Text}", settings);
                    image.Composite(textImage, actualX, actualY, CompositeOperator.Over);
                }
                else if (overlay is ImageOverlayRegion imgOverlay)
                {
                    if (string.IsNullOrWhiteSpace(imgOverlay.ImagePath) || !File.Exists(imgOverlay.ImagePath))
                        continue;

                    using var overlayImg = new MagickImage(imgOverlay.ImagePath);
                    var geometry = new MagickGeometry((uint)actualWidth, (uint)actualHeight)
                    {
                        IgnoreAspectRatio = true
                    };
                    overlayImg.Resize(geometry);

                    if (imgOverlay.Opacity < 100)
                    {
                        overlayImg.Alpha(AlphaOption.Set);
                        overlayImg.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, imgOverlay.Opacity / 100.0);
                    }

                    image.Composite(overlayImg, actualX, actualY, CompositeOperator.Over);
                }
            }

            var dir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            image.Write(OutputPath);
            StatusMessage = $"Saved to: {OutputPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error generating image: {ex.Message}";
        }
    }
}
