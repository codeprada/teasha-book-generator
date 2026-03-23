using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TeashaBookGenerator.Models;

public partial class ImageOverlayRegion : OverlayRegion
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(PreviewSource))]
    private string _imagePath = string.Empty;

    [ObservableProperty]
    private Bitmap? _previewSource;

    [ObservableProperty]
    private int _opacity = 100;

    public override string TypeIcon => "IMG";

    public override string Summary => string.IsNullOrWhiteSpace(ImagePath)
        ? "(no image)"
        : Path.GetFileName(ImagePath);

    public void LoadImage(string path)
    {
        ImagePath = path;
        PreviewSource = new Bitmap(path);
    }
}
