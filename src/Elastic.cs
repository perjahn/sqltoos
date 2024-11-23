using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace sqltoelastic
{
    class Elastic
    {
        static JsonSerializerOptions JsonOptionsRow { get; set; } = new() { WriteIndented = false };
        static JsonSerializerOptions JsonOptionsIndented { get; set; } = new() { WriteIndented = false };

        public static async Task<bool> PutIntoIndex(string serverurl, string cacertfile, bool allowInvalidHttpsCert, string username, string password, string indexname,
            string timestampfield, string idfield, string idprefix, JsonObject[] jsonrows)
        {
            if (allowInvalidHttpsCert)
            {
                using HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
                using HttpClient client = new(handler);

                return await PutIntoIndexWithClient(client, serverurl, username, password, indexname, timestampfield, idfield, idprefix, jsonrows);
            }
            else if (cacertfile != string.Empty)
            {
                using X509Certificate2 cacert = X509CertificateLoader.LoadCertificateFromFile(cacertfile);
                using HttpClientHandler handler = new()
                {
                    ServerCertificateCustomValidationCallback = (HttpRequestMessage message, X509Certificate2? cert, X509Chain? chain, SslPolicyErrors errors) =>
                        chain != null && chain.ChainElements.Count == 2 && chain.ChainElements[1].Certificate.RawData.SequenceEqual(cacert.RawData)
                };
                using HttpClient client = new(handler);

                return await PutIntoIndexWithClient(client, serverurl, username, password, indexname, timestampfield, idfield, idprefix, jsonrows);
            }
            else
            {
                using HttpClient client = new();

                return await PutIntoIndexWithClient(client, serverurl, username, password, indexname, timestampfield, idfield, idprefix, jsonrows);
            }
        }

        static async Task<bool> PutIntoIndexWithClient(HttpClient client, string serverurl, string username, string password, string indexname,
            string timestampfield, string idfield, string idprefix, JsonObject[] jsonrows)
        {
            var rownum = 0;
            Uri address = new($"{serverurl}/_bulk");
            StringBuilder rows = new();

            foreach (var jsonrow in jsonrows)
            {
                DateTime timestamp;
                if (jsonrow[timestampfield] is JsonValue timefield)
                {
                    timestamp = timefield.GetValue<DateTime>();
                    jsonrow["@timestamp"] = timestamp;
                }
                else
                {
                    Log($"Couldn't find timestamp field: '{timestampfield}'");
                    return false;
                }

                var datestring = timestamp.ToString("yyyy.MM");
                var dateindexname = $"{indexname}-{datestring}";
                var id = $"{idprefix}{jsonrow[idfield]?.GetValue<int>()}";

                var metadata = "{ \"index\": { \"_index\": \"" + dateindexname + "\", \"_id\": \"" + id + "\" } }";
                _ = rows.AppendLine(metadata);

                var rowdata = JsonSerializer.Serialize(jsonrow, JsonOptionsRow);
                Log($"'{rowdata}'");
                _ = rows.AppendLine(rowdata);

                rownum++;

                if (rownum % 10000 == 0)
                {
                    Log($"Importing rows: {rownum}");

                    var bulkdata = rows.ToString();
                    await ImportRows(client, address, username, password, bulkdata);
                    rows = new();
                }
            }

            var bulkdataLast = rows.ToString();
            if (bulkdataLast.Length > 0)
            {
                Log($"Importing rows: {rownum}");
                await ImportRows(client, address, username, password, bulkdataLast);
            }

            return true;
        }

        static int bulkdataCounter;

        static async Task ImportRows(HttpClient client, Uri address, string username, string password, string bulkdata)
        {
            if (username != string.Empty && password != string.Empty)
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new("Basic", credentials);
            }

            var result = string.Empty;
            try
            {
                await File.WriteAllTextAsync($"bulkdata_{bulkdataCounter++}.txt", bulkdata);

                using StringContent content = new(bulkdata, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(address, content);
                result = await response.Content.ReadAsStringAsync();
                LogResult(result);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Log($"Put '{address}': >>>{bulkdata}<<<");
                Log($"Result: >>>{result}<<<");
                Log($"Exception: >>>{ex}<<<");
            }
        }

        static void LogResult(string result)
        {
            if (!TryParseJsonObject(result, out JsonObject jsonresult))
            {
                Log(result);
                return;
            }
            if (jsonresult["items"] is not JsonArray items)
            {
                Log($"Result: '{jsonresult}'");
                return;
            }
            Dictionary<string, int> results = [];
            Dictionary<string, int> statuses = [];
            List<string> errors = [];

            foreach (var item in items)
            {
                if (item?["index"] is JsonObject index)
                {
                    if (index["result"] is JsonValue resultvalue)
                    {
                        var resultname = resultvalue.GetValue<string>();
                        results[resultname] = results.TryGetValue(resultname, out int value) ?
                            value + 1 :
                            results[resultname] = 1;
                    }

                    if (index["status"] is JsonValue statusvalue)
                    {
                        var statusname = statusvalue.GetValue<int>().ToString();
                        statuses[statusname] = statuses.TryGetValue(statusname, out int value) ? value + 1 : 1;
                    }

                    if (index["error"] is JsonObject error && error["reason"] is JsonValue reason)
                    {
                        errors.Add(reason.GetValue<string>());
                    }
                }
            }

            Log($"Results: {string.Join(", ", results.OrderBy(r => r.Key).Select(r => $"{r.Key}: {r.Value}"))}");
            Log($"Statuses: {string.Join(", ", statuses.OrderBy(s => s.Key).Select(s => $"{s.Key}: {s.Value}"))}");
            if (errors.Count > 0)
            {
                Log($"Got {errors.Count} errors:");
                foreach (var error in errors)
                {
                    Log(error);
                }
            }

            var filename = "result.json";
            Log($"Result saved to: '{filename}'");
            File.WriteAllText(filename, JsonSerializer.Serialize(jsonresult, JsonOptionsIndented));
        }

        static bool TryParseJsonObject(string json, out JsonObject jsonobject)
        {
            try
            {
                var n = JsonNode.Parse(json);
                if (n != null)
                {
                    jsonobject = n.AsObject();
                    return true;
                }
                jsonobject = [];
                return false;
            }
            catch (JsonException)
            {
                jsonobject = [];
                return false;
            }
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}
