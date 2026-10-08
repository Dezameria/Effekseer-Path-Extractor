using EffekseerAI.Host;

namespace ResourceManager.Tests;

public class EditorXmlComparerTests
{
    [Fact]
    public void AcceptsNativeRandomCenterRounding()
        => Assert.True(EditorXmlComparer.Equivalent("<Root><Center>0.1</Center></Root>", "<Root><Center>0.099999994</Center></Root>"));

    [Theory]
    [InlineData("<Root><Center>0.1</Center></Root>", "<Root><Center>0.10001</Center></Root>")]
    [InlineData("<Root><Name>0.1</Name></Root>", "<Root><Name>0.099999994</Name></Root>")]
    [InlineData("<Root><Texture>0.1</Texture></Root>", "<Root><Texture>0.099999994</Texture></Root>")]
    [InlineData("<Root><Center>0.1</Center></Root>", "<Root><Center>NaN</Center></Root>")]
    [InlineData("<Root><Center>0.1</Center></Root>", "<Other><Center>0.099999994</Center></Other>")]
    [InlineData("<Root id='1'><Center>0.1</Center></Root>", "<Root id='2'><Center>0.099999994</Center></Root>")]
    [InlineData("<Root><Center>1</Center></Root>", "<Root><Center>2</Center></Root>")]
    public void RejectsMeaningfulDifferences(string before, string after)
        => Assert.False(EditorXmlComparer.Equivalent(before, after));
}
