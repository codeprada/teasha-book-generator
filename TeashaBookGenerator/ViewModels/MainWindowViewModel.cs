using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;
using TeashaBookGenerator.Models;
using TeashaBookGenerator.Services;
using Velopack;

namespace TeashaBookGenerator.ViewModels;

public enum DrawingMode
{
    None,
    DrawText,
    DrawImage
}

/// <summary>Represents a single page in the book, holding its overlays and metadata.</summary>
public class BookPage : ObservableObject
{
    private string _title = "Untitled";
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private string _pageNumber = string.Empty;
    public string PageNumber
    {
        get => _pageNumber;
        set => SetProperty(ref _pageNumber, value);
    }

    public ObservableCollection<OverlayRegion> Overlays { get; } = [];

    public int TextCount { get; set; }
    public int ImageCount { get; set; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(PageNumber)
        ? Title
        : $"{Title} (p.{PageNumber})";
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPageTitle))]
    [NotifyPropertyChangedFor(nameof(CurrentPageNumber))]
    [NotifyPropertyChangedFor(nameof(PageNavigationText))]
    [NotifyPropertyChangedFor(nameof(HasPages))]
    [NotifyPropertyChangedFor(nameof(CanGoPreviousPage))]
    [NotifyPropertyChangedFor(nameof(CanGoNextPage))]
    private BookPage? _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageNavigationText))]
    [NotifyPropertyChangedFor(nameof(HasPages))]
    [NotifyPropertyChangedFor(nameof(CanGoPreviousPage))]
    [NotifyPropertyChangedFor(nameof(CanGoNextPage))]
    private int _currentPageIndex;

    public bool IsDrawingMode => DrawingMode != DrawingMode.None;

    public TextOverlayRegion? SelectedTextOverlay => SelectedOverlay as TextOverlayRegion;
    public ImageOverlayRegion? SelectedImageOverlay => SelectedOverlay as ImageOverlayRegion;
    public bool IsTextOverlaySelected => SelectedOverlay is TextOverlayRegion;
    public bool IsImageOverlaySelected => SelectedOverlay is ImageOverlayRegion;

    public ObservableCollection<OverlayRegion> Overlays { get; } = [];
    public ObservableCollection<BookPage> Pages { get; } = [];

    public ObservableCollection<EmbeddedFont> AvailableFonts { get; } = [];
    public ObservableCollection<string> AlignmentOptions { get; } = ["Left", "Center", "Right"];
    public ObservableCollection<int> FontSizes { get; } =
        [8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 48, 56, 64, 72, 96, 128];

    public bool HasPages => Pages.Count > 0;
    public bool CanGoPreviousPage => CurrentPageIndex > 0;
    public bool CanGoNextPage => CurrentPageIndex < Pages.Count - 1;

    public string PageNavigationText => Pages.Count > 0
        ? $"Page {CurrentPageIndex + 1} of {Pages.Count}"
        : "No pages";

    public string CurrentPageTitle
    {
        get => CurrentPage?.Title ?? string.Empty;
        set
        {
            if (CurrentPage != null)
            {
                CurrentPage.Title = value;
                OnPropertyChanged(nameof(CurrentPageTitle));
            }
        }
    }

    public string CurrentPageNumber
    {
        get => CurrentPage?.PageNumber ?? string.Empty;
        set
        {
            if (CurrentPage != null)
            {
                CurrentPage.PageNumber = value;
                OnPropertyChanged(nameof(CurrentPageNumber));
            }
        }
    }

    // ── Update properties ──

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _updateVersion = string.Empty;

    [ObservableProperty]
    private bool _isUpdateDownloading;

    [ObservableProperty]
    private int _updateDownloadProgress;

    private UpdateService? _updateService;
    private UpdateInfo? _pendingUpdateInfo;

    public event Action? PreviewInvalidated;

    private readonly Dictionary<string, string> _fontFileCache = new();

    public MainWindowViewModel()
    {
        Overlays.CollectionChanged += OnOverlaysCollectionChanged;
        InitializeFonts();
    }

    public MainWindowViewModel(UpdateService updateService) : this()
    {
        _updateService = updateService;
        _ = CheckForUpdateInBackgroundAsync();
    }

    private async Task CheckForUpdateInBackgroundAsync()
    {
        if (_updateService == null || !_updateService.IsInstalled) return;

        try
        {
            await Task.Delay(3000);
            UpdateInfo? updateInfo = await _updateService.CheckForUpdatesAsync();
            if (updateInfo != null)
            {
                _pendingUpdateInfo = updateInfo;
                UpdateVersion = updateInfo.TargetFullRelease.Version.ToString();
                IsUpdateAvailable = true;
            }
        }
        catch (Exception)
        {
            // Update check failures are non-fatal
        }
    }

    [RelayCommand]
    private async Task DownloadAndApplyUpdateAsync()
    {
        if (_updateService == null || _pendingUpdateInfo == null) return;

        try
        {
            IsUpdateDownloading = true;
            UpdateDownloadProgress = 0;

            await _updateService.DownloadUpdateAsync(_pendingUpdateInfo, progress =>
            {
                UpdateDownloadProgress = progress;
            });

            _updateService.ApplyUpdateAndRestart(_pendingUpdateInfo);
        }
        catch (Exception ex)
        {
            IsUpdateDownloading = false;
            StatusMessage = $"Update failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        IsUpdateAvailable = false;
    }

    private void InitializeFonts()
    {
        (string displayName, string fileName, string familyName)[] fonts = new (string displayName, string fileName, string familyName)[]
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

        foreach ((string displayName, string fileName, string familyName) in fonts)
        {
            string resourcePath = $"avares://TeashaBookGenerator/Assets/Fonts/{fileName}";
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
        if (_fontFileCache.TryGetValue(font.ResourcePath, out string? cached) && File.Exists(cached))
            return cached;

        Directory.CreateDirectory(FontCacheDir);

        Uri uri = new Uri(font.ResourcePath);
        Stream assets = Avalonia.Platform.AssetLoader.Open(uri);
        string fileName = Path.GetFileName(uri.AbsolutePath);
        string destPath = Path.Combine(FontCacheDir, fileName);

        using (FileStream fs = File.Create(destPath))
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
            Bitmap bitmap = new Bitmap(path);
            ImageSource = bitmap;
            OriginalImageWidth = bitmap.PixelSize.Width;
            OriginalImageHeight = bitmap.PixelSize.Height;
            IsImageLoaded = true;
            Overlays.Clear();
            SelectedOverlay = null;

            // Clear pages and add a default first page
            Pages.Clear();
            BookPage firstPage = new BookPage { Title = "Page 1" };
            Pages.Add(firstPage);
            CurrentPageIndex = 0;
            CurrentPage = firstPage;

            string dir = Path.GetDirectoryName(path) ?? ".";
            string name = Path.GetFileNameWithoutExtension(path);
            OutputPath = Path.Combine(dir, $"{name}_output");

            StatusMessage = $"Loaded: {Path.GetFileName(path)} ({OriginalImageWidth}x{OriginalImageHeight})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading image: {ex.Message}";
        }
    }

    #region Page Management

    /// <summary>
    /// Save the current page's overlays from the active Overlays collection back into the BookPage.
    /// </summary>
    private void SaveCurrentPageOverlays()
    {
        if (CurrentPage == null) return;
        CurrentPage.Overlays.Clear();
        foreach (OverlayRegion overlay in Overlays)
            CurrentPage.Overlays.Add(overlay);
    }

    /// <summary>
    /// Load overlays from a BookPage into the active Overlays collection.
    /// </summary>
    private void LoadPageOverlays(BookPage page)
    {
        Overlays.Clear();
        foreach (OverlayRegion overlay in page.Overlays)
            Overlays.Add(overlay);
        SelectedOverlay = Overlays.Count > 0 ? Overlays[0] : null;
    }

    /// <summary>Switch to a page by index. Saves current page overlays first.</summary>
    public void SwitchToPage(int index)
    {
        if (index < 0 || index >= Pages.Count) return;

        // Save current page state
        SaveCurrentPageOverlays();

        // Clear visuals before switching
        RequestClearVisuals?.Invoke();

        CurrentPageIndex = index;
        CurrentPage = Pages[index];

        // Load new page overlays
        LoadPageOverlays(CurrentPage);

        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageNumber));

        StatusMessage = $"Switched to: {CurrentPage.DisplayLabel}";
    }

    /// <summary>Raised when the view should clear overlay visuals (before page switch).</summary>
    public event Action? RequestClearVisuals;

    [RelayCommand]
    private void AddPage()
    {
        if (!IsImageLoaded) return;

        int pageNum = Pages.Count + 1;
        BookPage newPage = new BookPage
        {
            Title = $"Page {pageNum}"
        };

        // Save current page first
        SaveCurrentPageOverlays();
        RequestClearVisuals?.Invoke();

        Pages.Add(newPage);
        CurrentPageIndex = Pages.Count - 1;
        CurrentPage = newPage;

        // Clear overlays for the new empty page
        Overlays.Clear();
        SelectedOverlay = null;

        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageNumber));
        OnPropertyChanged(nameof(PageNavigationText));
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(CanGoPreviousPage));
        OnPropertyChanged(nameof(CanGoNextPage));

        StatusMessage = $"Added new page: {newPage.DisplayLabel}";
    }

    [RelayCommand]
    private void RemovePage()
    {
        if (Pages.Count <= 1)
        {
            StatusMessage = "Cannot remove the last page.";
            return;
        }

        RequestClearVisuals?.Invoke();

        string removedTitle = CurrentPage?.DisplayLabel ?? "page";
        Pages.RemoveAt(CurrentPageIndex);

        // Navigate to valid page
        int newIndex = Math.Min(CurrentPageIndex, Pages.Count - 1);
        CurrentPageIndex = newIndex;
        CurrentPage = Pages[newIndex];
        LoadPageOverlays(CurrentPage);

        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageNumber));
        OnPropertyChanged(nameof(PageNavigationText));
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(CanGoPreviousPage));
        OnPropertyChanged(nameof(CanGoNextPage));

        StatusMessage = $"Removed {removedTitle}";
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CanGoPreviousPage)
            SwitchToPage(CurrentPageIndex - 1);
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CanGoNextPage)
            SwitchToPage(CurrentPageIndex + 1);
    }

    #endregion

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
        if (CurrentPage == null) return;

        OverlayRegion overlay;
        if (DrawingMode == DrawingMode.DrawImage)
        {
            CurrentPage.ImageCount++;
            overlay = new ImageOverlayRegion
            {
                X = x, Y = y, Width = width, Height = height,
                Label = $"Image {CurrentPage.ImageCount}"
            };
        }
        else
        {
            CurrentPage.TextCount++;
            overlay = new TextOverlayRegion
            {
                X = x, Y = y, Width = width, Height = height,
                Label = $"Text {CurrentPage.TextCount}",
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
        int idx = Overlays.IndexOf(SelectedOverlay);
        Overlays.Remove(SelectedOverlay);
        SelectedOverlay = Overlays.Count > 0
            ? Overlays[Math.Min(idx, Overlays.Count - 1)]
            : null;
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        TopLevel? topLevel = App.TopLevel;
        if (topLevel == null) return;

        IReadOnlyList<IStorageFolder> folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Output Folder",
            AllowMultiple = false
        });

        if (folder.Count > 0)
            OutputPath = folder[0].Path.LocalPath;
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        TopLevel? topLevel = App.TopLevel;
        if (topLevel == null) return;

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
            // Ensure current page overlays are saved
            SaveCurrentPageOverlays();

            string projectPath = file.Path.LocalPath;
            string projectDir = Path.GetDirectoryName(projectPath) ?? ".";
            ProjectData project = new ProjectData
            {
                Version = 2,
                ImagePath = MakeRelativePath(projectDir, ImagePath),
                OutputPath = MakeRelativePath(projectDir, OutputPath),
            };

            foreach (BookPage page in Pages)
            {
                PageData pageData = new PageData
                {
                    Title = page.Title,
                    PageNumber = page.PageNumber,
                };

                foreach (OverlayRegion overlay in page.Overlays)
                {
                    if (overlay is TextOverlayRegion textOv)
                    {
                        pageData.Overlays.Add(new TextOverlayData
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
                        pageData.Overlays.Add(new ImageOverlayData
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

                project.Pages.Add(pageData);
            }

            string json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(projectPath, json);
            StatusMessage = $"Project saved: {Path.GetFileName(projectPath)} ({Pages.Count} page(s))";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving project: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        TopLevel? topLevel = App.TopLevel;
        if (topLevel == null) return;

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
            string projectPath = files[0].Path.LocalPath;
            string projectDir = Path.GetDirectoryName(projectPath) ?? ".";
            string json = await File.ReadAllTextAsync(projectPath);
            ProjectData? project = JsonSerializer.Deserialize<ProjectData>(json);

            if (project == null)
            {
                StatusMessage = "Invalid project file.";
                return;
            }

            // Migrate v1 projects: flat overlays → single page
            if (project.Pages.Count == 0 && project.Overlays is { Count: > 0 })
            {
                PageData page = new PageData { Title = "Page 1" };
                page.Overlays.AddRange(project.Overlays);
                project.Pages.Add(page);
            }

            // Ensure at least one page
            if (project.Pages.Count == 0)
                project.Pages.Add(new PageData { Title = "Page 1" });

            string imgPath = ResolveRelativePath(projectDir, project.ImagePath);
            if (!File.Exists(imgPath))
            {
                StatusMessage = $"Image not found: {imgPath}";
                return;
            }

            // Load background image (clears overlays and pages)
            LoadImageFromPath(imgPath);
            OutputPath = ResolveRelativePath(projectDir, project.OutputPath);

            // Signal to load all pages after layout
            _pendingProject = (project, projectDir);
            RequestLayoutThenRestore?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading project: {ex.Message}";
        }
    }

    private (ProjectData project, string projectDir)? _pendingProject;

    public event Action? RequestLayoutThenRestore;

    public void RestorePendingOverlays()
    {
        if (_pendingProject is not { } pending) return;
        _pendingProject = null;

        (ProjectData? project, string? projectDir) = pending;

        // Clear the default page created by LoadImageFromPath
        Pages.Clear();
        Overlays.Clear();

        foreach (PageData pageData in project.Pages)
        {
            BookPage bookPage = new BookPage
            {
                Title = pageData.Title,
                PageNumber = pageData.PageNumber,
            };

            foreach (OverlayData data in pageData.Overlays)
            {
                if (data is TextOverlayData textData)
                {
                    bookPage.TextCount++;
                    TextOverlayRegion overlay = new TextOverlayRegion
                    {
                        Label = string.IsNullOrEmpty(textData.Label) ? $"Text {bookPage.TextCount}" : textData.Label,
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
                    bookPage.Overlays.Add(overlay);
                }
                else if (data is ImageOverlayData imgData)
                {
                    bookPage.ImageCount++;
                    string imgPath = ResolveRelativePath(projectDir, imgData.ImagePath);
                    ImageOverlayRegion overlay = new ImageOverlayRegion
                    {
                        Label = string.IsNullOrEmpty(imgData.Label) ? $"Image {bookPage.ImageCount}" : imgData.Label,
                        X = imgData.X * ImageDisplayWidth,
                        Y = imgData.Y * ImageDisplayHeight,
                        Width = imgData.Width * ImageDisplayWidth,
                        Height = imgData.Height * ImageDisplayHeight,
                        Opacity = imgData.Opacity,
                    };

                    if (File.Exists(imgPath))
                        overlay.LoadImage(imgPath);

                    bookPage.Overlays.Add(overlay);
                }
            }

            Pages.Add(bookPage);
        }

        // Switch to the first page
        if (Pages.Count > 0)
        {
            CurrentPageIndex = 0;
            CurrentPage = Pages[0];
            LoadPageOverlays(Pages[0]);
        }

        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageNumber));
        OnPropertyChanged(nameof(PageNavigationText));
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(CanGoPreviousPage));
        OnPropertyChanged(nameof(CanGoNextPage));

        StatusMessage = $"Project loaded with {Pages.Count} page(s), {Pages.Sum(p => p.Overlays.Count)} overlay(s) total.";
    }

    private static string MakeRelativePath(string basePath, string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath)) return string.Empty;
        try
        {
            Uri baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            Uri targetUri = new Uri(targetPath);
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
        if (Pages.Count == 0) { StatusMessage = "No pages to export."; return; }
        if (string.IsNullOrWhiteSpace(OutputPath)) { StatusMessage = "Specify an output folder."; return; }

        // Ensure current page overlays are saved
        SaveCurrentPageOverlays();

        try
        {
            double scaleX = OriginalImageWidth / ImageDisplayWidth;
            double scaleY = OriginalImageHeight / ImageDisplayHeight;

            if (!Directory.Exists(OutputPath))
                Directory.CreateDirectory(OutputPath);

            string ext = Path.GetExtension(ImagePath);
            if (string.IsNullOrEmpty(ext)) ext = ".png";

            int imageNumber = 1;
            foreach (BookPage page in Pages)
            {
                using MagickImage image = new MagickImage(ImagePath);

                foreach (OverlayRegion overlay in page.Overlays)
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

                        MagickReadSettings settings = new MagickReadSettings
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

                        using MagickImage textImage = new MagickImage($"caption:{textOverlay.Text}", settings);
                        image.Composite(textImage, actualX, actualY, CompositeOperator.Over);
                    }
                    else if (overlay is ImageOverlayRegion imgOverlay)
                    {
                        if (string.IsNullOrWhiteSpace(imgOverlay.ImagePath) || !File.Exists(imgOverlay.ImagePath))
                            continue;

                        using MagickImage overlayImg = new MagickImage(imgOverlay.ImagePath);
                        MagickGeometry geometry = new MagickGeometry((uint)actualWidth, (uint)actualHeight)
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

                // Sanitize title for filename
                string safeTitle = string.Join("_", page.Title.Split(Path.GetInvalidFileNameChars()));
                string outputFile = Path.Combine(OutputPath, $"{imageNumber}_{safeTitle}{ext}");
                image.Write(outputFile);
                imageNumber++;
            }

            StatusMessage = $"Exported {Pages.Count} page(s) to: {OutputPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error generating images: {ex.Message}";
        }
    }
}
