using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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
                using var handler = new HttpClientHandler() { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
                using var client = new HttpClient(handler);

                return await PutIntoIndexWithClient(client, serverurl, username, password, indexname, timestampfield, idfield, idprefix, jsonrows);
            }
            else if (cacertfile != string.Empty)
            {
                using var cacert = new X509Certificate2(cacertfile);
                using var handler = new HttpClientHandler()
                {
                    ServerCertificateCustomValidationCallback = (HttpRequestMessage message, X509Certificate2? cert, X509Chain? chain, SslPolicyErrors errors) =>
                        chain != null && chain.ChainElements.Count == 2 && chain.ChainElements[1].Certificate.RawData.SequenceEqual(cacert.RawData)
                };
                using var client = new HttpClient(handler);

                return await PutIntoIndexWithClient(client, serverurl, username, password, indexname, timestampfield, idfield, idprefix, jsonrows);
            }
            else
            {
                using var client = new HttpClient();

                return await PutIntoIndexWithClient(client, serverurl, username, password, indexname, timestampfield, idfield, idprefix, jsonrows);
            }
        }

        static async Task<bool> PutIntoIndexWithClient(HttpClient client, string serverurl, string username, string password, string indexname,
            string timestampfield, string idfield, string idprefix, JsonObject[] jsonrows)
        {
            string bulkdata;

            var rownum = 0;
            var address = $"{serverurl}/_bulk";
            var rows = new StringBuilder();

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

                    bulkdata = rows.ToString();
                    await ImportRows(client, address, username, password, bulkdata);
                    rows = new StringBuilder();
                }
            }

            bulkdata = rows.ToString();
            if (bulkdata.Length > 0)
            {
                Log($"Importing rows: {rownum}");
                await ImportRows(client, address, username, password, bulkdata);
            }

            return true;
        }

        static int bulkdataCounter;

        static async Task ImportRows(HttpClient client, string address, string username, string password, string bulkdata)
        {
            if (username != string.Empty && password != string.Empty)
            {
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            var result = string.Empty;
            try
            {
                File.WriteAllText($"bulkdata_{bulkdataCounter++}.txt", bulkdata);

                using var content = new StringContent(bulkdata, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(address, content);
                result = await response.Content.ReadAsStringAsync();
                LogResult(result);
            }
            catch (Exception ex)
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
            var results = new Dictionary<string, int>();
            var statuses = new Dictionary<string, int>();
            var errors = new List<string>();

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

            string filename = "result.json";
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
