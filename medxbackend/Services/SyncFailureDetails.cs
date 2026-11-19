using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace medxbackend.Services;

/// <summary>Holds sanitized remote failure diagnostics for the current request, separate from API responses.</summary>
public sealed class SyncFailureDetails(IConfiguration configuration)
{
    private readonly Dictionary<string, string> failures = new(StringComparer.Ordinal);

    public string? Get(string system) => failures.GetValueOrDefault(system);
    public int? GetHttpStatus(string system) => Get(system) is string failure
        ? JsonNode.Parse(failure)?["httpStatus"]?.GetValue<int>() : null;
    public string? GetStage(string system) => Get(system) is string failure
        ? JsonNode.Parse(failure)?["stage"]?.GetValue<string>() : null;
    public void Clear(string system) => failures.Remove(system);

    public string Capture(
        string system, string stage, int? httpStatus, string body,
        object? patientPayload = null, params string?[] secrets)
    {
        var sensitiveValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "Lis:username", "Lis:password", "Crm:client_id", "Crm:client_secret" })
            Add(configuration[key]);
        foreach (var value in secrets)
            Add(value);
        if (patientPayload is not null)
            Collect(JsonSerializer.SerializeToElement(patientPayload));

        JsonNode? sanitized;
        try
        {
            sanitized = Sanitize(JsonNode.Parse(body));
        }
        catch (JsonException)
        {
            sanitized = JsonValue.Create(Redact(body));
        }

        var diagnostic = new JsonObject
        {
            ["system"] = system,
            ["stage"] = stage,
            ["httpStatus"] = httpStatus,
            ["responseBody"] = sanitized
        }.ToJsonString();
        failures[system] = diagnostic;
        return diagnostic;

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                sensitiveValues.Add(value);
        }

        void Collect(JsonElement element, bool sensitive = false)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                        Collect(property.Value, IsCredential(Normalize(property.Name)));
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        Collect(item, sensitive);
                    break;
                case JsonValueKind.String:
                    if (!sensitive)
                        break;
                    var value = element.GetString();
                    Add(value);
                    break;
            }
        }

        JsonNode? Sanitize(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                foreach (var key in obj.Select(pair => pair.Key).ToArray())
                {
                    var normalized = Normalize(key);
                    obj[key] = IsSensitive(normalized) ? JsonValue.Create("[REDACTED]") : Sanitize(obj[key]?.DeepClone());
                }
                return obj;
            }
            if (node is JsonArray array)
                return new JsonArray(array.Select(item => Sanitize(item?.DeepClone())).ToArray());
            if (node is JsonValue scalar && scalar.TryGetValue<string>(out var text))
                return JsonValue.Create(Redact(text));
            return node;
        }

        static string Normalize(string key) =>
            Regex.Replace(key, "[^a-zA-Z0-9]", "").ToLowerInvariant();

        string Redact(string text)
        {
            foreach (var value in sensitiveValues.OrderByDescending(value => value.Length))
                text = text.Replace(value, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
            text = Regex.Replace(text, @"(?i)\bBearer\s+[^\s,;""'<>]+", "Bearer [REDACTED]");
            text = Regex.Replace(text,
                @"(?i)\b(access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|client[_-]?id|password|authorization|cookie)\b[""']?\s*[:=]\s*(""[^""]*""|'[^']*'|[^\r\n,;<]+)",
                "$1=[REDACTED]");
            text = Regex.Replace(text,
                @"(?is)<(access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|password|authorization|cookie)\b[^>]*>.*?</\1\s*>",
                "<$1>[REDACTED]</$1>");
            return text;
        }
    }

    private static bool IsCredential(string key) =>
        key.Contains("token") || key.Contains("password") || key.Contains("secret")
        || key.Contains("authorization") || key.Contains("cookie") || key.Contains("credential")
        || key is "clientid" or "username";

    private static bool IsSensitive(string key) =>
        IsCredential(key) || key.Contains("patient") || key.Contains("hkid") || key.Contains("passport")
        || key.Contains("birth") || key.Contains("email") || key.Contains("phone")
        || key.Contains("mobile") || key.Contains("address") || key.Contains("labno")
        || key.Contains("dailyno") || key.Contains("comment") || key.Contains("remarks")
        || key is "clientid" or "username" or "dob" or "id" or "labid" or "labno"
            or "dailyno" or "sid" or "refnumber" or "englishname" or "chinesename"
            or "idpassportno" or "idpassportnoc" or "dateofbirthc" or "englishnamec" or "chinesenamec"
            or "fax" or "sms" or "whatsapp";
}
