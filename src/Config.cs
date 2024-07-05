
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace sqltoelastic
{
    class Config
    {
        public string Dbprovider { get; set; } = string.Empty;
        public string Connstr { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;

        public string[] Toupperfields { get; set; } = [];
        public string[] Tolowerfields { get; set; } = [];
        public string[] Addconstantfields { get; set; } = [];
        public string[] Expandjsonfields { get; set; } = [];
        public string[] Deescapefields { get; set; } = [];

        public string Elasticserverurl { get; set; } = string.Empty;
        public string Cacertfile { get; set; } = string.Empty;
        public bool Allowinvalidhttpscert { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Indexname { get; set; } = string.Empty;
        public string Timestampfield { get; set; } = string.Empty;
        public string Idfield { get; set; } = string.Empty;
        public string Idprefix { get; set; } = string.Empty;
    }

    class EnvironmentVariableConverter<T> : JsonConverter<T> where T : new()
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var obj = new T();
            var properties = typeof(T).GetProperties();

            using var document = JsonDocument.ParseValue(ref reader);

            var root = document.RootElement;

            foreach (var property in properties)
            {
                if (root.TryGetProperty(property.Name.ToLower(), out JsonElement element))
                {
                    var envValue = Environment.GetEnvironmentVariable($"SQLTOELASTIC_{property.Name.ToUpper()}");
                    if (!string.IsNullOrEmpty(envValue))
                    {
                        property.SetValue(obj, property.PropertyType.IsArray ? envValue.Split(',') : envValue);
                    }
                    else
                    {
                        var value = JsonSerializer.Deserialize(element.GetRawText(), property.PropertyType, options);
                        property.SetValue(obj, value);
                    }
                }
            }

            return obj;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
