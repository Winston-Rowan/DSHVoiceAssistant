using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DivaVoiceAssistant.Models;
using DivaVoiceAssistant.Utils;

namespace DivaVoiceAssistant.Config;

/// <summary>appsettings.json 的根节点</summary>
public sealed class AppConfig
{
    [JsonPropertyName("DivaConfig")]
    public DivaConfig DivaConfig { get; set; } = new();
}

/// <summary>
/// 配置读写服务。配置文件位于程序目录下的 Config\appsettings.json。
/// 支持通过环境变量 DIVA_API_KEY 覆盖密钥（避免密钥入库泄露）。
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ConfigPath { get; } = Path.Combine(AppContext.BaseDirectory, "Config", "appsettings.json");

    /// <summary>加载配置；文件缺失或损坏时回退到默认配置并落盘。</summary>
    public DivaConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var root = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Options);
                if (root?.DivaConfig != null)
                {
                    Logger.Info("配置已加载: " + ConfigPath);
                    return ApplyEnvOverrides(root.DivaConfig);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("配置文件解析失败，使用默认配置: " + ex.Message);
        }

        var defaults = new DivaConfig();
        Save(defaults);
        return ApplyEnvOverrides(defaults);
    }

    /// <summary>保存配置到 appsettings.json。</summary>
    public void Save(DivaConfig config)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new AppConfig { DivaConfig = config }, Options));
            Logger.Info("配置已保存: " + ConfigPath);
        }
        catch (Exception ex)
        {
            Logger.Error("保存配置失败: " + ex.Message);
        }
    }

    private static DivaConfig ApplyEnvOverrides(DivaConfig config)
    {
        var envKey = Environment.GetEnvironmentVariable("DIVA_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey)) config.ApiKey = envKey;
        return config;
    }
}
