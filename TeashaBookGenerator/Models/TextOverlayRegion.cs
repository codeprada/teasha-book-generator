using CommunityToolkit.Mvvm.ComponentModel;

namespace TeashaBookGenerator.Models;

public partial class TextOverlayRegion : OverlayRegion
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _text = string.Empty;

    [ObservableProperty]
    private EmbeddedFont? _selectedFont;

    [ObservableProperty]
    private int _fontSize = 24;

    [ObservableProperty]
    private string _fontColor = "#000000";

    [ObservableProperty]
    private string _alignment = "Left";

    public override string TypeIcon => "T";

    public override string Summary => string.IsNullOrWhiteSpace(Text)
        ? "(empty)"
        : (Text.Length > 30 ? Text[..30] + "..." : Text);
}
