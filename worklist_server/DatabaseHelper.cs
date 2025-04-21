using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using FellowOakDicom;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace worklist_server
{

     public class DicomQueryConfig
    {
        // Static properties
        public static string Table { get; set; }
        public static List<string> Conditions { get; set; } = new List<string>();
        public static Dictionary<string, string> Mapping { get; set; } = new Dictionary<string, string>();

        public const string JsonPath = "dicom_query_config.json";

        static DicomQueryConfig()
        {
            LoadConfig();
        }

        public static void LoadConfig()
        {
            if (!File.Exists(JsonPath))
            {
                ResetToDefaults();
                SaveConfig();
                Console.WriteLine("⚠ Tạo file config mới với giá trị mặc định.");
                return;
            }

            try
            {
                string json = File.ReadAllText(JsonPath);
                var tempConfig = JsonConvert.DeserializeObject<TemporaryConfig>(json);

                if (tempConfig != null)
                {
                    Table = tempConfig.Table;
                    Mapping = tempConfig.Mapping ?? new Dictionary<string, string>();
                    Conditions = tempConfig.Conditions ?? new List<string>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Lỗi đọc file config: {ex.Message}");
                ResetToDefaults();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                var tempConfig = new TemporaryConfig
                {
                    Table = Table,
                    Mapping = Mapping,
                    Conditions = Conditions
                };

                string json = JsonConvert.SerializeObject(tempConfig, Formatting.Indented);
                File.WriteAllText(JsonPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Lỗi lưu file config: {ex.Message}");
            }
        }

        private static void ResetToDefaults()
        {
            Table = "";
            Mapping = new Dictionary<string, string>
        {
         //   { "(0010,0020)", "PatientID" }
        };
            Conditions = new List<string>
        {
         //   "(@Modality = '' OR Modality = @Modality)"
        };
        }

        public static void AddMappingWithCondition(string dicomTag, string dbField)
        {
            Mapping[dicomTag] = dbField;
            string condition = $"(@{dbField} = '' OR {dbField} = @{dbField})";

            if (Conditions.Count > 0 && !Conditions.Contains(condition))
            {
                condition = "AND " + condition;
            }

            // Chỉ thêm vào Conditions nếu chưa tồn tại
            if (!Conditions.Contains(condition))
            {
                Conditions.Add(condition);
            }

        }

        public static bool RemoveMappingWithCondition(string dicomTag)
        {
            if (Mapping.TryGetValue(dicomTag, out var dbField))
            {
                Mapping.Remove(dicomTag);
                string conditionToRemove = $"(@{dbField} = '' OR {dbField} = @{dbField})";
                Conditions.RemoveAll(c => c == conditionToRemove);
                return true;
            }
            return false;
        }

        public static void CleanOrphanedConditions()
        {
            var mappedFields = Mapping.Values.ToList();
            Conditions.RemoveAll(condition =>
            {
                var match = Regex.Match(condition, @"OR\s+(\w+)\s*=");
                return match.Success && !mappedFields.Contains(match.Groups[1].Value);
            });
        }

        // Helper class chỉ dùng cho serialization/deserialization
        private class TemporaryConfig
        {
            public string Table { get; set; }
            public Dictionary<string, string> Mapping { get; set; }
            public List<string> Conditions { get; set; }
        }
    }
    public class DatabaseHelper : IDisposable
    {

        private readonly SqlConnection _connection;
        public class ProcedureData
        {
            public string PatientID { get; set; }
            public string PatientName { get; set; }
            public string PatientBirthDate { get; set; }
            public string PatientSex { get; set; }
            public string AccessionNumber { get; set; }
            public string Modality { get; set; }
            public string ScheduledStationAETitle { get; set; }
            public string ScheduledDate { get; set; }
            public string ScheduledTime { get; set; }
        }

        public DatabaseHelper()
        {
            try
            {
                _connection = new SqlConnection(ConfigManager.Settings.Database.ConnectionString);
                DicomQueryConfig.LoadConfig();
                _connection.Open();
            }catch(Exception ex)
            {
               // MessageBox.Show(ex.Message);

            }
            
        }
     

        public List<DicomDataset> GetMatchingProceduresSQL(DicomDataset request)
        {
            var results = new List<DicomDataset>();
            var selectedFields = new List<string>(DicomQueryConfig.Mapping.Values);
            var conditions = new List<string>(DicomQueryConfig.Conditions);
            var parameters = new Dictionary<string, object>();

            // Duyệt qua từng DICOM Tag trong request.Dataset
            foreach (var item in request)
            {
                DicomTag tag = item.Tag; // Lấy DicomTag
                string tagKey = tag.ToString(); // Lấy giá trị DicomTag dạng "(Group, Element)"
                // So sánh với key trong DicomQueryConfig.Mapping
                if (DicomQueryConfig.Mapping.ContainsKey(tagKey))
                {
                    string sqlColumnName = DicomQueryConfig.Mapping[tagKey]; // Lấy cột SQL tương ứng
                    // Lấy giá trị của Tag từ request.Dataset
                    string value = request.GetSingleValueOrDefault(tag, "");
                    // Gán vào parameters dù giá trị có thể rỗng
                    parameters[$"@{sqlColumnName}"] = value;
               //     MessageBox.Show($"✅ Gán {sqlColumnName} = '{value}' (từ {tagName})");
                }
                else
                {
                    // Nếu không tìm thấy tagKey trong Mapping
                   // MessageBox.Show($"⚠ Không tìm thấy mapping cho {tagKey}");
                }
            }


            // 🔹 Đảm bảo tất cả biến trong Conditions đều có giá trị
            foreach (var condition in DicomQueryConfig.Conditions)
            {
                var matches = Regex.Matches(condition, @"@\w+"); // Lấy tất cả biến SQL (@Modality, @AETitle, ...)
                foreach (Match match in matches)
                {
                    string paramName = match.Value;
                    if (!parameters.ContainsKey(paramName)) // Nếu chưa có thì thêm giá trị mặc định
                    {
                        parameters[paramName] = "";
                    }
                }
            }

            // 🔹 Xây dựng câu lệnh SQL an toàn
            string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" ", conditions) : "";
            string sqlQuery = $"SELECT {string.Join(", ", selectedFields)} FROM {DicomQueryConfig.Table}{whereClause}";        

            try
            {
                // 🔹 Thực thi truy vấn
                using (var cmd = new SqlCommand(sqlQuery, _connection))
                {
                    foreach (var param in parameters)
                    {
                        cmd.Parameters.AddWithValue(param.Key, param.Value);
                    }

                    using (var reader = cmd.ExecuteReader())
                    {
                        // 🔹 Lấy danh sách cột SQL thực tế có trong bảng
                        HashSet<string> actualColumns = new HashSet<string>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            actualColumns.Add(reader.GetName(i).Trim());
                        }

                        while (reader.Read())
                        {
                            var dataset = CreateDicomResponse(reader, actualColumns); // Truyền danh sách cột vào hàm
                            results.Add(dataset);
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                if (ex.Message.Contains("Invalid column name"))
                {
                    WorklistService.Log($"❌ Error details: {ex.Message} in Database");

                }
                else
                {
                    WorklistService.Log($"❌ Error SQL Other: {ex.Message}");
                }
            }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.Contains("Invalid column name"))
                {
                    WorklistService.Log($"❌ Error details: {ex.Message} in Database");

                }
                else
                {
                    WorklistService.Log($"❌ Error SQL Other: {ex.Message}");
                }
                
            }


            return results;
        }


        //    private DicomDataset CreateDicomResponse(SqlDataReader reader, HashSet<string> actualColumns)
        //    {
        //        var dataset = new DicomDataset();

        //        var vrMaxLengths = new Dictionary<DicomVR, int>
        //{
        //    { DicomVR.PN, 64 },
        //    { DicomVR.LO, 64 },
        //    { DicomVR.SH, 16 },
        //    { DicomVR.CS, 16 },
        //    { DicomVR.UI, 64 },
        //    { DicomVR.TM, 16 }
        //};

        //        // Các định dạng ngày có thể gặp trong database
        //        string[] acceptedDateFormats = { "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy/MM/dd" };
        //        string[] acceptedDateTimeFormats = { "yyyyMMddHHmmss", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss" };

        //        foreach (var kvp in DicomQueryConfig.Mapping)
        //        {
        //            string dicomKeyword = kvp.Key;
        //            string sqlColumnName = kvp.Value;

        //            try
        //            {
        //                DicomTag tag;
        //                try
        //                {
        //                    tag = DicomTag.Parse(dicomKeyword);
        //                }
        //                catch (Exception ex)
        //                {
        //                    WorklistService.Log($"⚠ Không tìm thấy DicomTag hợp lệ: {dicomKeyword} | {ex.Message}");
        //                    continue;
        //                }

        //                if (!actualColumns.Contains(sqlColumnName))
        //                {
        //                    WorklistService.Log($"⚠ Cột SQL không tồn tại trong database: {sqlColumnName}");
        //                    continue;
        //                }

        //                string value = reader[sqlColumnName].ToString().Trim();
        //                value = string.Join(" ", value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        //                DicomDictionaryEntry entry = DicomDictionary.Default[tag];
        //                var vr = entry.ValueRepresentations.FirstOrDefault();

        //                if (vr == DicomVR.DA || vr == DicomVR.DT)
        //                {
        //                    DateTime dt;

        //                    bool parsed = false;
        //                    if (reader[sqlColumnName] is DateTime dtRaw)
        //                    {
        //                        dt = dtRaw;
        //                        parsed = true;
        //                    }
        //                    else
        //                    {
        //                        var formats = (vr == DicomVR.DA) ? acceptedDateFormats : acceptedDateTimeFormats;
        //                        parsed = DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);

        //                        // fallback TryParse nếu format không khớp
        //                        if (!parsed)
        //                            parsed = DateTime.TryParse(value, out dt);
        //                    }

        //                    if (!parsed)
        //                    {
        //                        WorklistService.Log($"⚠ Không thể parse {vr.Code}: {value} cho tag {dicomKeyword}");
        //                        continue;
        //                    }

        //                    value = (vr == DicomVR.DA) ? dt.ToString("yyyyMMdd") : dt.ToString("yyyyMMddHHmmss");
        //                }

        //                if (vr == DicomVR.DA)
        //                {
        //                    if (!string.IsNullOrWhiteSpace(value) && DateTime.TryParse(value, out DateTime dateValue))
        //                    {
        //                        value = dateValue.ToString("yyyyMMdd");
        //                    }
        //                    else
        //                    {
        //                        WorklistService.Log($"⚠ Không thể parse DA: \"{value}\" cho tag {tag}");
        //                        continue;
        //                    }
        //                }

        //                if (vrMaxLengths.TryGetValue(vr, out int maxLength) && value.Length > maxLength)
        //                {
        //                    WorklistService.Log($"⚠ Trường {dicomKeyword} (VR: {vr.Code}) vượt quá {maxLength} ký tự, sẽ bị cắt ngắn");
        //                    value = value.Substring(0, maxLength);
        //                }

        //                dataset.AddOrUpdate(tag, value);
        //            }
        //            catch (Exception ex)
        //            {
        //                WorklistService.Log($"❌ Lỗi khi xử lý tag {dicomKeyword} - SQL Column: {sqlColumnName} | {ex}");
        //            }
        //        }

        //        return dataset;
        //    }

        //    private DicomDataset CreateDicomResponse(SqlDataReader reader, HashSet<string> actualColumns)
        //    {
        //        var dataset = new DicomDataset();

        //        var vrMaxLengths = new Dictionary<DicomVR, int>
        //{
        //    { DicomVR.PN, 64 },
        //    { DicomVR.LO, 64 },
        //    { DicomVR.SH, 16 },
        //    { DicomVR.CS, 16 },
        //    { DicomVR.UI, 64 },
        //    { DicomVR.TM, 16 },
        //    { DicomVR.IS, 12 },
        //    { DicomVR.DS, 16 },
        //    { DicomVR.AS, 4 }
        //};

        //        string[] acceptedDateFormats = {
        //    "yyyyMMdd", "yyyy-MM-dd", "yyyy/MM/dd",
        //    "dd/MM/yyyy", "MM/dd/yyyy",
        //    "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
        //    "yyyy-MM-dd HH:mm:ss.fff"
        //};

        //        string[] acceptedDateTimeFormats = {
        //    "yyyyMMddHHmmss", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
        //    "yyyy-MM-dd HH:mm:ss.fff"
        //};

        //        foreach (var kvp in DicomQueryConfig.Mapping)
        //        {
        //            string dicomKeyword = kvp.Key;
        //            string sqlColumnName = kvp.Value;

        //            try
        //            {
        //                DicomTag tag;
        //                try
        //                {
        //                    tag = DicomTag.Parse(dicomKeyword);
        //                }
        //                catch (Exception ex)
        //                {
        //                    WorklistService.Log($"⚠ Không tìm thấy DicomTag hợp lệ: {dicomKeyword} | {ex.Message}");
        //                    continue;
        //                }

        //                if (!actualColumns.Contains(sqlColumnName))
        //                {
        //                    WorklistService.Log($"⚠ Cột SQL không tồn tại trong database: {sqlColumnName}");
        //                    continue;
        //                }

        //                string value = reader[sqlColumnName]?.ToString()?.Trim() ?? "";
        //                value = string.Join(" ", value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        //                DicomDictionaryEntry entry = DicomDictionary.Default[tag];
        //                var vr = entry.ValueRepresentations.FirstOrDefault();

        //                // 🕒 Xử lý ngày / giờ
        //                if (vr == DicomVR.DA || vr == DicomVR.DT)
        //                {
        //                    DateTime dt;
        //                    bool parsed = false;

        //                    if (reader[sqlColumnName] is DateTime dtRaw)
        //                    {
        //                        dt = dtRaw;
        //                        parsed = true;
        //                    }
        //                    else
        //                    {
        //                        var formats = (vr == DicomVR.DA) ? acceptedDateFormats : acceptedDateTimeFormats;

        //                        parsed = DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dt);

        //                        if (!parsed)
        //                        {
        //                            parsed = DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dt);
        //                        }
        //                    }

        //                    if (!parsed)
        //                    {
        //                        WorklistService.Log($"⚠ Không thể parse {vr.Code}: \"{value}\" cho tag {tag}");
        //                        continue;
        //                    }

        //                    value = (vr == DicomVR.DA) ? dt.ToString("yyyyMMdd") : dt.ToString("yyyyMMddHHmmss");
        //                }
        //                // 🔢 Xử lý kiểu số IS / DS
        //                else if (vr == DicomVR.IS)
        //                {
        //                    if (!int.TryParse(value, out int intVal))
        //                    {
        //                        WorklistService.Log($"⚠ Không thể parse IS (int): \"{value}\" cho tag {tag}");
        //                        continue;
        //                    }
        //                    value = intVal.ToString();
        //                }
        //                else if (vr == DicomVR.DS)
        //                {
        //                    if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double dblVal))
        //                    {
        //                        WorklistService.Log($"⚠ Không thể parse DS (double): \"{value}\" cho tag {tag}");
        //                        continue;
        //                    }
        //                    value = dblVal.ToString("G", CultureInfo.InvariantCulture);
        //                }
        //                // 👴 Xử lý tuổi AS
        //                else if (vr == DicomVR.AS)
        //                {
        //                    if (value.Length != 4 || !(value.EndsWith("Y") || value.EndsWith("M") || value.EndsWith("W") || value.EndsWith("D")))
        //                    {
        //                        WorklistService.Log($"⚠ Dữ liệu AS không hợp lệ: \"{value}\" cho tag {tag}");
        //                        continue;
        //                    }
        //                }
        //                // ❌ Không hỗ trợ Sequence
        //                else if (vr == DicomVR.SQ)
        //                {
        //                    WorklistService.Log($"⚠ Tag {dicomKeyword} có VR là Sequence (SQ) - chưa xử lý.");
        //                    continue;
        //                }

        //                // ✂ Giới hạn độ dài
        //                if (vrMaxLengths.TryGetValue(vr, out int maxLength) && value.Length > maxLength)
        //                {
        //                    WorklistService.Log($"⚠ Trường {dicomKeyword} (VR: {vr.Code}) vượt quá {maxLength} ký tự, sẽ bị cắt ngắn");
        //                    value = value.Substring(0, maxLength);
        //                }

        //                dataset.AddOrUpdate(tag, value);
        //            }
        //            catch (Exception ex)
        //            {
        //                WorklistService.Log($"❌ Lỗi khi xử lý tag {dicomKeyword} - SQL Column: {sqlColumnName} | {ex}");
        //            }
        //        }

        //        return dataset;
        //    }


        private DicomDataset CreateDicomResponse(SqlDataReader reader, HashSet<string> actualColumns)
        {
            var dataset = new DicomDataset();

            var vrMaxLengths = new Dictionary<DicomVR, int>
    {
        { DicomVR.PN, 64 },
        { DicomVR.LO, 64 },
        { DicomVR.SH, 16 },
        { DicomVR.CS, 16 },
        { DicomVR.UI, 64 },
        { DicomVR.TM, 16 },
        { DicomVR.IS, 12 },
        { DicomVR.DS, 16 },
        { DicomVR.AS, 4 }
    };

            string[] acceptedDateFormats = { "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy/MM/dd" };
            string[] acceptedDateTimeFormats = { "yyyyMMddHHmmss", "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-ddTHH:mm:ss" };

            foreach (var kvp in DicomQueryConfig.Mapping)
            {
                string dicomKeyword = kvp.Key;
                string sqlColumnName = kvp.Value;

                try
                {
                    DicomTag tag;
                    try
                    {
                        tag = DicomTag.Parse(dicomKeyword);
                    }
                    catch (Exception ex)
                    {
                        WorklistService.Log($"⚠ Không tìm thấy DicomTag hợp lệ: {dicomKeyword} | {ex.Message}");
                        continue;
                    }

                    if (!actualColumns.Contains(sqlColumnName))
                    {
                        WorklistService.Log($"⚠ Cột SQL không tồn tại trong database: {sqlColumnName}");
                        continue;
                    }

                    string rawValue = reader[sqlColumnName]?.ToString()?.Trim() ?? "";
                    string value = string.Join(" ", rawValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        WorklistService.Log($"⚠ Giá trị rỗng cho tag {tag}, bỏ qua.");
                        continue;
                    }

                    DicomDictionaryEntry entry = DicomDictionary.Default[tag];
                    var vr = entry.ValueRepresentations.FirstOrDefault();

                    // 🕒 Xử lý ngày / giờ
                    if (vr == DicomVR.DA || vr == DicomVR.DT)
                    {
                        DateTime dt;
                        bool parsed = false;

                        if (reader[sqlColumnName] is DateTime dtRaw)
                        {
                            dt = dtRaw;
                            parsed = true;
                        }
                        else
                        {
                            var formats = (vr == DicomVR.DA) ? acceptedDateFormats : acceptedDateTimeFormats;

                            parsed = DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dt)
                                  || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dt);
                        }

                        if (!parsed)
                        {
                            WorklistService.Log($"⚠ Không thể parse {vr.Code}: \"{value}\" cho tag {tag}");
                            continue;
                        }

                        value = (vr == DicomVR.DA) ? dt.ToString("yyyyMMdd") : dt.ToString("yyyyMMddHHmmss");
                    }
                    // 🔢 Xử lý kiểu số IS / DS
                    else if (vr == DicomVR.IS)
                    {
                        if (!int.TryParse(value, out int intVal))
                        {
                            WorklistService.Log($"⚠ Không thể parse IS (int): \"{value}\" cho tag {tag}");
                            continue;
                        }
                        value = intVal.ToString();
                    }
                    else if (vr == DicomVR.DS)
                    {
                        if (!double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double dblVal))
                        {
                            WorklistService.Log($"⚠ Không thể parse DS (double): \"{value}\" cho tag {tag}");
                            continue;
                        }
                        value = dblVal.ToString("G", CultureInfo.InvariantCulture);
                    }
                    // 👴 Xử lý tuổi AS
                    else if (vr == DicomVR.AS)
                    {
                        if (value.Length != 4 || !(value.EndsWith("Y") || value.EndsWith("M") || value.EndsWith("W") || value.EndsWith("D")))
                        {
                            WorklistService.Log($"⚠ Dữ liệu AS không hợp lệ: \"{value}\" cho tag {tag}");
                            continue;
                        }
                    }
                    // ❌ Không hỗ trợ Sequence
                    else if (vr == DicomVR.SQ)
                    {
                        WorklistService.Log($"⚠ Tag {dicomKeyword} có VR là Sequence (SQ) - chưa xử lý.");
                        continue;
                    }

                    // ✂ Giới hạn độ dài
                    if (vrMaxLengths.TryGetValue(vr, out int maxLength) && value.Length > maxLength)
                    {
                        WorklistService.Log($"⚠ Trường {dicomKeyword} (VR: {vr.Code}) vượt quá {maxLength} ký tự, sẽ bị cắt ngắn");
                        value = value.Substring(0, maxLength);
                    }

                    dataset.AddOrUpdate(tag, value);
                }
                catch (Exception ex)
                {
                    WorklistService.Log($"❌ Lỗi khi xử lý tag {dicomKeyword} - SQL Column: {sqlColumnName} | {ex}");
                }
            }

            return dataset;
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}