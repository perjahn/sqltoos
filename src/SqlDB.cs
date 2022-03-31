using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sqltoelastic
{
    class SqlDB
    {
        public static async Task<JObject[]> GetRows(string dbprovider, string connstr, string sql,
            string[] toupperfields, string[] tolowerfields,
            string[] addconstantfields, string[] expandjsonfields, string[] deescapefields)
        {
            DbProviderFactory factory = dbprovider switch
            {
                "mysql" => MySqlClientFactory.Instance,
                "postgres" => NpgsqlFactory.Instance,
                "sqlserver" => SqlClientFactory.Instance,
                _ => throw new Exception()
            };

            using DbConnection? cn = factory.CreateConnection();
            if (cn == null)
            {
                throw new Exception();
            }

            cn.ConnectionString = connstr;
            await cn.OpenAsync();

            using var cmd = cn.CreateCommand();

            cmd.Connection = cn;
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = sql;

            using var reader = await cmd.ExecuteReaderAsync();

            var jsonrows = await ReadData(reader, toupperfields, tolowerfields, addconstantfields, expandjsonfields, deescapefields);

            await reader.CloseAsync();

            await cn.CloseAsync();

            return jsonrows;
        }

        static async Task<JObject[]> ReadData(DbDataReader reader,
            string[] toupperfields, string[] tolowerfields,
            string[] addconstantfields, string[] expandjsonfields, string[] deescapefields)
        {
            var addfields = addconstantfields.Where(f => f.Contains('=')).ToDictionary(f => f.Split('=')[0], f => f.Split('=')[1]);

            List<string> columns = new List<string>();

            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            List<JObject> jsonrows = new List<JObject>();

            while (await reader.ReadAsync())
            {
                var rowdata = new StringBuilder();

                rowdata.Append('{');

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    string colname = columns[i];
                    bool lastcol = i == reader.FieldCount - 1;

                    if (reader.IsDBNull(i))
                    {
                        continue;
                    }

                    if (reader.GetFieldType(i) == typeof(DateTimeOffset))
                    {
                        DateTimeOffset? data = reader.GetValue(i) as DateTimeOffset?;
                        rowdata.Append($"\"{colname}\":\"{(data == null ? "null" : data.Value.ToString("s"))}\"");
                    }
                    else if (reader.GetFieldType(i) == typeof(DateTime))
                    {
                        DateTime? data = reader.GetValue(i) as DateTime?;
                        rowdata.Append($"\"{colname}\":\"{(data == null ? "null" : data.Value.ToString("s"))}\"");
                    }
                    else if (reader.GetFieldType(i) == typeof(short))
                    {
                        short? data = reader.GetValue(i) as short?;
                        rowdata.Append($"\"{colname}\":{(data == null ? "\"null\"" : data.Value.ToString())}");
                    }
                    else if (reader.GetFieldType(i) == typeof(int))
                    {
                        int? data = reader.GetValue(i) as int?;
                        rowdata.Append($"\"{colname}\":{(data == null ? "\"null\"" : data.Value.ToString())}");
                    }
                    else if (reader.GetFieldType(i) == typeof(long))
                    {
                        long? data = reader.GetValue(i) as long?;
                        rowdata.Append($"\"{colname}\":{(data == null ? "\"null\"" : data.Value.ToString())}");
                    }
                    else if (reader.GetValue(i) is string data)
                    {
                        if (expandjsonfields.Contains(colname))
                        {
                            try
                            {
                                JObject jsondata = JObject.Parse(data);
                                data = jsondata.ToString(Formatting.Indented);

                                rowdata.Append($"\"{colname}\":{data}");
                            }
                            catch (JsonReaderException)
                            {
                                // Parse. Or Parse not. There is no TryParse. :(
                                rowdata.Append($"\"{colname}\":\"{data}\"");
                            }
                        }
                        else
                        {
                            if (toupperfields.Contains(colname))
                            {
                                data = data.ToUpper();
                            }
                            if (tolowerfields.Contains(colname))
                            {
                                data = data.ToLower();
                            }
                            if (!deescapefields.Contains(colname))
                            {
                                data = data.Replace(@"\", @"\\").Replace("\"", "\\\"");
                            }

                            rowdata.Append($"\"{colname}\":\"{data}\"");
                        }
                    }
                    else
                    {
                        rowdata.Append($"\"{colname}\":null");
                    }

                    if (!lastcol)
                    {
                        rowdata.Append(',');
                    }
                }

                foreach (var addfield in addfields)
                {
                    rowdata.Append(',');

                    string addfieldname = addfield.Key;
                    string addfieldvalue = addfield.Value;

                    if (DateTimeOffset.TryParse(addfieldvalue, out DateTimeOffset valuedatetimeoffset))
                    {
                        rowdata.Append($"\"{addfieldname}\":\"{valuedatetimeoffset.ToString("s")}\"");
                    }
                    else if (DateTime.TryParse(addfieldvalue, out DateTime valuedatetime))
                    {
                        rowdata.Append($"\"{addfieldname}\":\"{valuedatetime.ToString("s")}\"");
                    }
                    else if (short.TryParse(addfieldvalue, out short valueshort))
                    {
                        rowdata.Append($"\"{addfieldname}\":{valueshort}");
                    }
                    else if (int.TryParse(addfieldvalue, out int valueint))
                    {
                        rowdata.Append($"\"{addfieldname}\":{valueint}");
                    }
                    else if (long.TryParse(addfieldvalue, out long valuelong))
                    {
                        rowdata.Append($"\"{addfieldname}\":{valuelong}");
                    }
                    else
                    {
                        rowdata.Append($"\"{addfieldname}\":\"{addfieldvalue}\"");
                    }
                }

                rowdata.AppendLine("}");

                JObject jsonrow = JObject.Parse(rowdata.ToString());
                jsonrows.Add(jsonrow);
            }

            return jsonrows.ToArray();
        }

        static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
}
