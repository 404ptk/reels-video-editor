using System.Collections.Generic;

namespace ReelsVideoEditor.App.ViewModels.Text;

public sealed class TextViewModel : ViewModelBase
{
    public string Title { get; } = "Text";

    public string Description { get; } = "Drag the preset onto the timeline to add text to your video.";

    public IReadOnlyList<Models.TextPresetDefinition> Presets { get; } =
    [
        new("Sunset", "Inter", 56, "#FF6B6B"),
        new("Ocean", "Inter", 56, "#3A86FF"),
        new("Mint", "Inter", 56, "#2EC4B6")
    ];
}
