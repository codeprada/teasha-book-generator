using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TeashaBookGenerator.Views;

public class AddPageResult
{
    public string Title { get; set; } = string.Empty;
    public string PageNumber { get; set; } = string.Empty;
}

public partial class AddPageDialog : Window
{
    public AddPageDialog()
    {
        InitializeComponent();
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        TextBox? titleBox = this.FindControl<TextBox>("TitleBox");
        TextBox? pageNumberBox = this.FindControl<TextBox>("PageNumberBox");

        string? title = titleBox?.Text?.Trim();
        if (string.IsNullOrEmpty(title))
            title = "Untitled";

        Close(new AddPageResult
        {
            Title = title,
            PageNumber = pageNumberBox?.Text?.Trim() ?? string.Empty
        });
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
