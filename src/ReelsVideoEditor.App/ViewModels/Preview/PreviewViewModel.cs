namespace ReelsVideoEditor.App.ViewModels.Preview;

public sealed class PreviewViewModel : ViewModelBase
{
    public string Title { get; } = "Preview";

    public string PlaceholderTitle { get; } = "No video loaded";

    public string PlaceholderSubtitle { get; } = "Drop a video here";
}
