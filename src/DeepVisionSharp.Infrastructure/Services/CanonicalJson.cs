using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// 保持历史 canonical JSON 合同：非 ASCII、不加空格、按键排序。
/// IR digest 必须对原始 <see cref="JsonObject"/> 哈希，禁止 VO→JSON→hash。
/// </summary>
internal static class CanonicalJson
{
    public static string Render(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return "null";
        }

        if (node is JsonValue value)
        {
            return value.ToJsonString();
        }

        if (node is JsonArray array)
        {
            var items = new List<string>(array.Count);
            foreach (var item in array)
            {
                items.Add(Render(item));
            }

            return "[" + string.Join(",", items) + "]";
        }

        if (node is JsonObject obj)
        {
            var parts = obj
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => JsonSerializer.Serialize(pair.Key) + ":" + Render(pair.Value));
            return "{" + string.Join(",", parts) + "}";
        }

        return node.ToJsonString();
    }

    public static string Sha256Hex(JsonNode? node)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Render(node)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
