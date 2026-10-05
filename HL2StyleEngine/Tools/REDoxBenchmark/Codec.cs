using System.Text;
using System.Text.Json;
#if NET10_0_OR_GREATER
using REDox.Serialization.SystemTextJson;
using REDox.Serialization;
#endif

namespace Engine.Editor.Level;

// This adapter is compiled into the experimental Engine.Editor only.
public static class BenchmarkCodec
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip
    };
    public static string Selected => Environment.GetEnvironmentVariable("HS2_BENCH_CODEC") ?? "stj";
    public static string[] Names =>
#if NET10_0_OR_GREATER
        ["stj", "stj-utf8", "redox-json", "redox-parallel", "redox-dox"];
#else
        ["stj", "stj-utf8"];
#endif

#if NET10_0_OR_GREATER
    // Do not charge REDox initialization to the System.Text.Json control runs.
    private static class RedoxSettings
    {
        internal static readonly SystemTextJsonSerializerSettings Sequential = new(Options);
        internal static readonly SystemTextJsonSerializerSettings Parallel = new(Options)
        {
            ParallelOptions = new ParallelDeserializeOptions { ParallelDeserializeEnabled = true }
        };
    }
    private static readonly REDox.Json.JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true, EnableValueValidation = true, MaxDepth = 64
    };
#endif

    public static object Read(byte[] bytes, string text, Type type, string codec) => codec switch
    {
        "stj" => JsonSerializer.Deserialize(text, type, Options)!,
        "stj-utf8" => JsonSerializer.Deserialize(bytes, type, Options)!,
#if NET10_0_OR_GREATER
        "redox-json" => REDox.Json.JsonSerializer.Deserialize(bytes, type, RedoxSettings.Sequential, JsonOptions)!,
        "redox-parallel" => REDox.Json.JsonSerializer.Deserialize(bytes, type, RedoxSettings.Parallel, JsonOptions)!,
        "redox-dox" => REDox.DoxSerializer.Deserialize(bytes, type, RedoxSettings.Sequential)!,
#endif
        _ => throw new ArgumentException("Unsupported codec: " + codec)
    };

    public static byte[] Write(object value, Type type, string codec) => codec switch
    {
        "stj" => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, type, Options)),
        "stj-utf8" => JsonSerializer.SerializeToUtf8Bytes(value, type, Options),
#if NET10_0_OR_GREATER
        "redox-json" or "redox-parallel" => REDox.Json.JsonSerializer.SerializeToUtf8Bytes(value, type, RedoxSettings.Sequential,
            new REDox.Json.JsonWriteOptions { WriteIndented = true, IndentSize = 2 }),
        "redox-dox" => REDox.DoxSerializer.Serialize(value, type, RedoxSettings.Sequential),
#endif
        _ => throw new ArgumentException("Unsupported codec: " + codec)
    };

    public static string CachePath(string path) => Path.Combine(
        Environment.GetEnvironmentVariable("HS2_BENCH_CACHE") ?? throw new InvalidOperationException("Set HS2_BENCH_CACHE."),
        Path.GetFileNameWithoutExtension(path) + ".dox");

    public static LevelFile ReadFile(string path, string codec)
    {
        if (codec == "stj") return (LevelFile)Read([], File.ReadAllText(path), typeof(LevelFile), codec);
        return (LevelFile)Read(File.ReadAllBytes(codec == "redox-dox" ? CachePath(path) : path), "", typeof(LevelFile), codec);
    }
}
