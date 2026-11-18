namespace Visual.Core;

/// <summary>
/// A video or picture feeding an ISF image input. Each host makes its own (Renderer.OpenMedia);
/// every call happens on the GL thread. Should not throw: problems go to Error.
/// </summary>
public interface IMediaSource : IDisposable
{
    /// <summary>Advances playback and puts the newest frame in Texture. Once per drawn frame, with the renderer's quad bound.</summary>
    void Update(double dt, float speed);
    /// <summary>0 = nothing to show yet.</summary>
    uint Texture { get; }
    int Width { get; }
    int Height { get; }
    /// <summary>First row in memory is the top of the picture (CPU uploads); the shader flips it.</summary>
    bool TopDown { get; }
    string? Error { get; }
}

public static class Media
{
    public static readonly string[] Videos = [".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".3gp", ".ts"];
    public static readonly string[] Images = [".png", ".jpg", ".jpeg", ".bmp", ".webp"];

    public static bool IsUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static bool IsImage(string name) => Images.Contains(Path.GetExtension(name).ToLowerInvariant());

    /// <summary>A plain file name with a known video / picture extension (no folders: it comes from the remote).</summary>
    public static bool ValidName(string name) =>
        name.Length is > 0 and <= 200 && name == Path.GetFileName(name) && !name.StartsWith('.')
        && name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0']) < 0
        && (Videos.Contains(Path.GetExtension(name).ToLowerInvariant()) || IsImage(name));

    public static string[] List(string folder) => Directory.Exists(folder)
        ? Directory.GetFiles(folder).Select(f => Path.GetFileName(f)).Where(ValidName).Order().ToArray()
        : [];

    /// <summary>What to open for a scene's Src: the URL itself, or the file in the media folder; null if not allowed.</summary>
    public static string? Resolve(string folder, string src) =>
        IsUrl(src) ? (Uri.TryCreate(src, UriKind.Absolute, out var u) && u.Host != "" ? src : null)
        : ValidName(src) ? Path.Combine(folder, src) : null;
}
