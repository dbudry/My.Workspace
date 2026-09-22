using My.Functions.Services;
using Xunit;

namespace My.Tests.Services;

public class HeicJpegConverterTests
{
    [Fact]
    public void TryConvert_returns_false_for_garbage()
    {
        Assert.False(HeicJpegConverter.TryConvert([0x00, 0x01, 0x02], out var jpeg));
        Assert.Empty(jpeg);
    }

    [Fact]
    public void TryConvert_accepts_jpeg()
    {
        var jpeg = Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAn/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAGfAD//2Q==");
        Assert.True(HeicJpegConverter.TryConvert(jpeg, out var outJpeg));
        Assert.True(outJpeg.Length > 0);
        Assert.Equal(0xFF, outJpeg[0]);
        Assert.Equal(0xD8, outJpeg[1]);
    }
}
