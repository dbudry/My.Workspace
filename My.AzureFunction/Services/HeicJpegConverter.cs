using ImageMagick;

namespace My.Functions.Services;

public static class HeicJpegConverter
{
    public static bool TryConvert(byte[] input, out byte[] jpeg)
    {
        jpeg = [];
        if (input is not { Length: > 0 })
            return false;

        try
        {
            using var image = new MagickImage(input);
            image.AutoOrient();
            image.Format = MagickFormat.Jpeg;
            image.Quality = 90;
            jpeg = image.ToByteArray(MagickFormat.Jpeg);
            return jpeg.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
