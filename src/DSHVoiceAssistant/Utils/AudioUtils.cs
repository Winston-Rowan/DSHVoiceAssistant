using System.IO;
using System.Text;

namespace DSHVoiceAssistant.Utils;

/// <summary>音频处理工具：RMS 计算、WAV 封装等。</summary>
public static class AudioUtils
{
    /// <summary>
    /// 计算 16bit PCM 数据的归一化 RMS（0 ~ 1），用于 VAD 与波形可视化。
    /// </summary>
    public static float ComputeRms(byte[] pcm16)
    {
        if (pcm16 == null || pcm16.Length < 2) return 0f;

        var count = pcm16.Length / 2;
        double sum = 0;
        for (var i = 0; i < count; i++)
        {
            var sample = (short)(pcm16[i * 2] | (pcm16[i * 2 + 1] << 8));
            sum += (double)sample * sample;
        }
        return (float)Math.Sqrt(sum / count) / 32768f;
    }

    /// <summary>
    /// 对 16bit PCM 应用数字增益（返回新数组，不改动原数据；增益 ≤1 时原样返回）。
    /// 带削波钳位（±32767）。用于低增益麦克风的适配：在采集源头统一放大，
    /// VAD / 唤醒词门控 / 云端识别 / 本地识别全部受益。
    /// </summary>
    public static byte[] ApplyGain(byte[] pcm16, double gain)
    {
        if (pcm16.Length < 2 || gain <= 1.0) return pcm16;

        var count = pcm16.Length / 2;
        var result = new byte[pcm16.Length];
        for (var i = 0; i < count; i++)
        {
            var sample = (short)(pcm16[i * 2] | (pcm16[i * 2 + 1] << 8));
            var scaled = (int)(sample * gain);
            if (scaled > 32767) scaled = 32767;
            else if (scaled < -32768) scaled = -32768;

            var s = (short)scaled;
            result[i * 2] = (byte)(s & 0xFF);
            result[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return result;
    }

    /// <summary>
    /// 将若干 PCM 16bit 数据块封装为完整的 WAV 字节流（含 44 字节头）。
    /// </summary>
    public static byte[] BuildWavBytes(IEnumerable<byte[]> chunks, int sampleRate = 16000, short channels = 1, short bitsPerSample = 16)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        var dataLength = chunks.Sum(c => c?.Length ?? 0);
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataLength);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);                    // fmt 块长度
        bw.Write((short)1);              // PCM 编码
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataLength);

        foreach (var chunk in chunks)
        {
            if (chunk is { Length: > 0 }) bw.Write(chunk);
        }
        bw.Flush();
        return ms.ToArray();
    }
}
