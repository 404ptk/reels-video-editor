namespace ReelsVideoEditor.App.Models;

public static class TextRevealEffect
{
    public const string None = "None";
    public const string Pop = "Pop";

    public static string Normalize(string? value)
    {
        if (string.Equals(value, Pop, System.StringComparison.OrdinalIgnoreCase))
        {
            return Pop;
        }

        return None;
    }
}
