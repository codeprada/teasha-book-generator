using Avalonia.Media;

namespace TeashaBookGenerator.Models;

public class EmbeddedFont
{
    public required string DisplayName { get; init; }
    public required FontFamily FontFamily { get; init; }
    public required string ResourcePath { get; init; }

    public override string ToString() => DisplayName;
}
