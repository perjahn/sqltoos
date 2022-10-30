using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace sqltoelastic
{
    class Elastic
    {
        public static async Task<bool> PutIntoIndex(string serverurl, string cacertfile, bool allowInvalidHttpsCert, string username, string password, string indexname,
            string timestampfield, string idfield, string idprefix, JObject[] jsonrows)
        {
            string bulkdata;

            using var handler = new HttpClientHandler();

            if (allowInvalidHttpsCert)
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }
            else if (cacertfile != string.Empty)
            {
                var cacert = new X509Certificate2(cacertfile);

                handler.ServerCertificateCustomValidationCallback = (
                    HttpRequestMessage message,
                    X509Certificate2? cert,
                    X509Chain? chain,
                    SslPolicyErrors errors
                ) => chain != null && chain.ChainElements.Count == 2 && chain.ChainElements[1].Certificate.RawData.SequenceEqual(cacert.RawData);
            }

            using var client = allowInvalidHttpsCert || cacertfile != string.Empty ? new HttpClient(handler) : new HttpClient();

            int rownum = 0;

            string address = $"{serverurl}/_bulk";

            var rows = new StringBuilder();

            foreach (var jsonrow in jsonrows)
            {
                jsonrow["@timestamp"] = jsonrow[timestampfield];
                DateTime timestamp;
                if (jsonrow[timestampfield] is JValue timefield)
                {
                    timestamp = timefield.Value<DateTime>();
                }
                else
                {
                    Log($"Couldn't find timestamp field: '{timestampfield}'");
                    return false;
                }

                string datestring = timestamp.ToString("yyyy.MM");
                string dateindexname = $"{indexname}-{datestring}";
                string id = $"{idprefix}{jsonrow[idfield]?.Value<string>()}";

                string metadata = "{ \"index\": { \"_index\": \"" + dateindexname + "\", \"_id\": \"" + id + "\" } }";
                rows.AppendLine(metadata);

                string rowdata = jsonrow.ToString(Formatting.None);
                Log($"'{rowdata}'");
                rows.AppendLine(rowdata);

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

        static int bulkdataCounter = 0;

        static async Task ImportRows(HttpClient client, string address, string username, string password, string bulkdata)
        {
            if (username != string.Empty && password != string.Empty)
            {
                string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            string result = string.Empty;
            try
            {
                File.WriteAllText($"bulkdata_{bulkdataCounter++}.txt", bulkdata);

                var content = new StringContent(bulkdata, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(address, content);
                result = await response.Content.ReadAsStringAsync();
                LogResult(result);
            }
            catch (Exception ex)
            {
                Log($"Put '{address}': >>>{bulkdata}<<<");
                Log($"Result: >>>{result}<<<");
                Log($"Exception: >>>{ex.ToString()}<<<");
            }
        }

        static void LogResult(string result)
        {
            if (TryParseJObject(result, out JObject jsonresult))
            {
                if (jsonresult["items"] is JArray items)
                {
                    var results = new Dictionary<string, int>();
                    var statuses = new Dictionary<string, int>();
                    var errors = new List<string>();

                    foreach (var item in items)
                    {
                        if (item["index"] is JObject index)
                        {
                            if (index["result"] is JValue resultvalue)
                            {
                                string resultname = resultvalue.Value<string>() ?? string.Empty;
                                if (results.ContainsKey(resultname))
                                {
                                    results[resultname]++;
                                }
                                else
                                {
                                    results[resultname] = 1;
                                }
                            }

                            if (index["status"] is JValue statusvalue)
                            {
                                string statusname = statusvalue.Value<string>() ?? string.Empty;
                                if (statuses.ContainsKey(statusname))
                                {
                                    statuses[statusname]++;
                                }
                                else
                                {
                                    statuses[statusname] = 1;
                                }
                            }

                            if (index["error"] is JObject error)
                            {
                                if (error["reason"] is JValue reason)
                                {
                                    errors.Add(reason.Value<string>() ?? string.Empty);
                                }
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
                    File.WriteAllText(filename, jsonresult.ToString(Formatting.Indented));
                }
                else
                {
                    Log($"Result: '{jsonresult}'");
                }
            }
            else
            {
                Log(result);
            }
        }

        static bool TryParseJObject(string json, out JObject jobject)
        {
            try
            {
                jobject = JObject.Parse(json);
                return true;
            }
            catch (JsonReaderException)
            {
                jobject = new JObject();
                return false;
            }
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}
