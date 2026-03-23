using CommunityToolkit.Mvvm.ComponentModel;

namespace TeashaBookGenerator.Models;

public abstract partial class OverlayRegion : ObservableObject
{
    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    [ObservableProperty]
    private string _label = string.Empty;

    public abstract string TypeIcon { get; }
    public abstract string Summary { get; }
}
