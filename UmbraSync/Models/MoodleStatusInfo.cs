using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace UmbraSync.Models;

public partial class MoodleStatusInfo
{
    [JsonPropertyName("IconID")]
    public int IconID { get; set; }

    [JsonPropertyName("Title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("Description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("Type")]
    public int Type { get; set; }

    public string CleanTitle => TagRegex().Replace(Title, string.Empty).Trim();

    public string CleanDescription => TagRegex().Replace(Description, string.Empty).Trim();

    [GeneratedRegex(@"\[/?(?:color|glow|i|b|u)(?:=[^\]]*)?]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TagRegex();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static List<MoodleStatusInfo> ParseMoodles(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return [];

        try
        {
            var trimmed = data.AsSpan().Trim();
            if (trimmed.Length == 0) return [];

            if (trimmed[0] == '[')
                return JsonSerializer.Deserialize<List<MoodleStatusInfo>>(data, JsonOptions) ?? [];

            if (trimmed[0] == '{')
            {
                var manager = JsonSerializer.Deserialize<MoodleStatusManager>(data, JsonOptions);
                return manager?.Statuses ?? [];
            }

            return ParseFromMemoryPack(data);
        }
        catch
        {
            return [];
        }
    }
    
    private static List<MoodleStatusInfo> ParseFromMemoryPack(string base64)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch { return []; }

        if (bytes.Length < 4) return [];

        var result = new List<MoodleStatusInfo>();
        int offset = 0;

        int count = ReadInt32(bytes, ref offset);
        if (count <= 0 || count > 100) return [];

        for (int i = 0; i < count && offset < bytes.Length; i++)
        {
            try
            {
                if (offset >= bytes.Length) break;
                offset += 1;
                if (!Skip(bytes, ref offset, 16)) break;
                int iconId = ReadInt32(bytes, ref offset);
                string title = ReadUtf16String(bytes, ref offset);
                string description = ReadUtf16String(bytes, ref offset);
                ReadUtf16String(bytes, ref offset);
                
                if (!Skip(bytes, ref offset, 8)) break;
                int type = ReadInt32(bytes, ref offset);
                if (!Skip(bytes, ref offset, 4)) break;

                if (!Skip(bytes, ref offset, 4)) break;
                if (!Skip(bytes, ref offset, 4)) break;
                if (!Skip(bytes, ref offset, 16)) break;
                if (!Skip(bytes, ref offset, 4)) break;
                ReadUtf16String(bytes, ref offset);
                ReadUtf16String(bytes, ref offset);

                if (iconId > 0)
                {
                    result.Add(new MoodleStatusInfo
                    {
                        IconID = iconId,
                        Title = title,
                        Description = description,
                        Type = type
                    });
                }
            }
            catch
            {
                break;
            }
        }

        return result;
    }

    private static int ReadInt32(byte[] bytes, ref int offset)
    {
        if (offset + 4 > bytes.Length) { offset = bytes.Length; return 0; }
        int val = BitConverter.ToInt32(bytes, offset);
        offset += 4;
        return val;
    }

    private static bool Skip(byte[] bytes, ref int offset, int count)
    {
        if (offset + count > bytes.Length) { offset = bytes.Length; return false; }
        offset += count;
        return true;
    }

    private static string ReadUtf16String(byte[] bytes, ref int offset)
    {
        if (offset + 4 > bytes.Length) { offset = bytes.Length; return string.Empty; }

        int charCount = BitConverter.ToInt32(bytes, offset);
        offset += 4;

        if (charCount < 0) return string.Empty; // null string
        if (charCount == 0) return string.Empty;

        int byteCount = charCount * 2;
        if (offset + byteCount > bytes.Length) { offset = bytes.Length; return string.Empty; }

        string result = Encoding.Unicode.GetString(bytes, offset, byteCount);
        offset += byteCount;
        return result;
    }
}

internal class MoodleStatusManager
{
    [JsonPropertyName("Statuses")]
    public List<MoodleStatusInfo> Statuses { get; set; } = [];
}
