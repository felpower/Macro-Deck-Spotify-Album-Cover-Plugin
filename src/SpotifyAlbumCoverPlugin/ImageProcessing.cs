using System.Drawing;
using System.Drawing.Drawing2D;
using System.Security.Cryptography;
using System.Text;

namespace SpotifyAlbumCoverPlugin;

public static class ImageProcessing
{
    public static Image ToSquareImage(Image source, int size)
    {
        var side = Math.Min(source.Width, source.Height);
        var srcX = (source.Width - side) / 2;
        var srcY = (source.Height - side) / 2;
        var srcRect = new Rectangle(srcX, srcY, side, side);

        var dest = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(dest);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, size, size), srcRect, GraphicsUnit.Pixel);

        return dest;
    }

    public static string CreateDeterministicIconId(string title, string artist)
    {
        var input = $"{title}|{artist}";
        return CreateDeterministicIconIdFromString(input);
    }

    public static string CreateDeterministicIconIdFromString(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new Guid(guidBytes).ToString();
    }
}


