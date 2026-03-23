using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TeashaBookGenerator.Models;

public class ProjectData
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("imagePath")]
    public string ImagePath { get; set; } = string.Empty;

    [JsonPropertyName("outputPath")]
    public string OutputPath { get; set; } = string.Empty;

    [JsonPropertyName("overlays")]
    public List<OverlayData> Overlays { get; set; } = [];
}

[JsonDerivedType(typeof(TextOverlayData), "text")]
[JsonDerivedType(typeof(ImageOverlayData), "image")]
public class OverlayData
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Stored as fraction (0-1) of original image width.</summary>
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

public class TextOverlayData : OverlayData
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("fontName")]
    public string FontName { get; set; } = string.Empty;

    [JsonPropertyName("fontSize")]
    public int FontSize { get; set; } = 24;

    [JsonPropertyName("fontColor")]
    public string FontColor { get; set; } = "#000000";

    [JsonPropertyName("alignment")]
    public string Alignment { get; set; } = "Left";
}

public class ImageOverlayData : OverlayData
{
    [JsonPropertyName("imagePath")]
    public string ImagePath { get; set; } = string.Empty;

    [JsonPropertyName("opacity")]
    public int Opacity { get; set; } = 100;
}
