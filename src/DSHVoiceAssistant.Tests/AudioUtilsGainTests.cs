using DSHVoiceAssistant.Utils;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>数字增益（低增益麦克风适配）测试</summary>
public class AudioUtilsGainTests
{
    private static byte[] MakePcm(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            bytes[i * 2] = (byte)(samples[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
        }
        return bytes;
    }

    private static short ReadSample(byte[] pcm, int index)
        => (short)(pcm[index * 2] | (pcm[index * 2 + 1] << 8));

    [Fact]
    public void ApplyGain_GainOne_ReturnsOriginal()
    {
        var pcm = MakePcm(1000, -2000, 500);
        Assert.Same(pcm, AudioUtils.ApplyGain(pcm, 1.0));
    }

    [Fact]
    public void ApplyGain_GainLessThanOne_ReturnsOriginal()
    {
        var pcm = MakePcm(1000);
        Assert.Same(pcm, AudioUtils.ApplyGain(pcm, 0.5));
    }

    [Fact]
    public void ApplyGain_Silence_StaysSilence()
    {
        var pcm = MakePcm(0, 0, 0);
        var gained = AudioUtils.ApplyGain(pcm, 3.0);
        Assert.Equal(0f, AudioUtils.ComputeRms(gained));
    }

    [Fact]
    public void ApplyGain_ScalesSamples()
    {
        var pcm = MakePcm(1000, -2000);
        var gained = AudioUtils.ApplyGain(pcm, 3.0);
        Assert.Equal(3000, ReadSample(gained, 0));
        Assert.Equal(-6000, ReadSample(gained, 1));
    }

    [Fact]
    public void ApplyGain_ClipsAtPositiveMaximum()
    {
        var pcm = MakePcm(30000);
        var gained = AudioUtils.ApplyGain(pcm, 3.0);
        Assert.Equal(32767, ReadSample(gained, 0));
    }

    [Fact]
    public void ApplyGain_ClipsAtNegativeMaximum()
    {
        var pcm = MakePcm(-30000);
        var gained = AudioUtils.ApplyGain(pcm, 3.0);
        Assert.Equal(-32768, ReadSample(gained, 0));
    }

    [Fact]
    public void ApplyGain_PreservesLength()
    {
        var pcm = MakePcm(1, 2, 3, 4);
        Assert.Equal(pcm.Length, AudioUtils.ApplyGain(pcm, 2.5).Length);
    }

    [Fact]
    public void ApplyGain_DoesNotMutateInput()
    {
        var pcm = MakePcm(1000, 2000);
        var copy = (byte[])pcm.Clone();
        AudioUtils.ApplyGain(pcm, 4.0);
        Assert.Equal(copy, pcm);
    }

    [Fact]
    public void ApplyGain_RmsScalesApproximately()
    {
        var pcm = MakePcm(1000, -1000, 1000, -1000);
        var before = AudioUtils.ComputeRms(pcm);
        var after = AudioUtils.ComputeRms(AudioUtils.ApplyGain(pcm, 4.0));
        Assert.True(after > before * 3.9 && after <= before * 4.1, $"RMS {before} → {after}");
    }
}
