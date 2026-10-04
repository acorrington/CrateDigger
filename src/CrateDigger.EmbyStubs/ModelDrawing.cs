// Verified against Emby 4.10.1.0 (probe round 13/14): plugins-page thumbnails.

namespace MediaBrowser.Model.Drawing
{
    /// <summary>Verified enum members + values.</summary>
    public enum ImageFormat
    {
        Bmp = 0,
        Gif = 1,
        Jpg = 2,
        Png = 3,
        Webp = 4,
        Avif = 5,
    }
}

namespace MediaBrowser.Common.Plugins
{
    using MediaBrowser.Model.Drawing;

    /// <summary>
    /// Implemented by plugins that provide a plugins-page thumbnail
    /// (GET /Plugins/{Id}/Thumb). The handler reads this interface WITHOUT a
    /// null-guard — a plugin missing it makes the endpoint 500 (verified live).
    /// First-party example: MBBackup.Plugin implements it and streams an
    /// embedded "{Namespace}.thumb.png".
    /// </summary>
    public interface IHasThumbImage
    {
        /// <summary>Format used for the Content-Type "image/{format}" response header.</summary>
        ImageFormat ThumbImageFormat { get; }

        /// <summary>The thumbnail bytes (typically an embedded resource stream).</summary>
        Stream GetThumbImage();
    }
}