using Microsoft.AspNetCore.Mvc;
using SignalTracker.Models; // Your DbContext model & model DTOs (e.g., PolygonDto, PredictionPointDto, etc.)
using System.Linq;
using System.Threading.Tasks;
using SignalTracker.Helper;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore.Storage;   // for GetDbTransaction()
using System.Data.Common;   
                   // for DbTransaction


namespace SignalTracker.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MapViewController : BaseController
    {
        private readonly IWebHostEnvironment _env;

        ApplicationDbContext db = null;
        CommonFunction cf = null;
        public MapViewController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor, IWebHostEnvironment env)
        {
            db = context;
            _env = env;
            cf = new CommonFunction(context, httpContextAccessor);
        }

        // ==================== User/Session endpoints (unchanged) ====================

        public class UserModel
        {
            public string name { get; set; }
            public string mobile { get; set; }
            public string make { get; set; }
            public string model { get; set; }
            public string os { get; set; }
            public string operator_name { get; set; }
            public string? device_id { get; set; }
            public string? gcm_id { get; set; }
            public int? company_id { get; set; }
        }

        [HttpPost]
        [AllowAnonymous]
        public JsonResult user_signup([FromBody] UserModel model)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();

            try
            {
                if (model != null && model.device_id != null)
                {
                    var existingUser1 = db.tbl_user.FirstOrDefault(u => u.device_id == model.device_id);
                    if (existingUser1 != null)
                    {
                        message.Status = 1;
                        message.Message = "This device is already registered as - " + existingUser1.name;
                        message.Data = new { userid = existingUser1.id };
                    }
                }

                var existingUser = db.tbl_user.FirstOrDefault(u => u.mobile == model.mobile && u.make == model.make);

                if (existingUser != null)
                {
                    message.Status = 1;
                    message.Message = "User already exists.";
                    message.Data = new { userid = existingUser.id };
                }
                else
                {
                    var newUser = new tbl_user
                    {
                        name = model.name,
                        mobile = model.mobile,
                        make = model.make,
                        model = model.model,
                        os = model.os,
                        operator_name = model.operator_name,
                        device_id = model.device_id,
                        gcm_id = model.gcm_id,
                        company_id = model.company_id
                    };

                    db.tbl_user.Add(newUser);
                    db.SaveChanges();

                    message.Status = 1;
                    message.Message = "User saved successfully.";
                    message.Data = new { userid = newUser.id };
                }
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = "Error: " + ex.Message;
            }

            return Json(message);
        }

        public class SessionStartModel
        {
            public int userid { get; set; }
            public string start_time { get; set; }
            public string type { get; set; }
            public string? notes { get; set; }
        }

        [HttpPost]
        [AllowAnonymous]
        public JsonResult start_session([FromBody] SessionStartModel model)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();

            try
            {
                var newSess = new tbl_session
                {
                    user_id = model.userid,
                    start_time = DateTime.TryParse(model.start_time, out var ts) ? ts : (DateTime?)null,
                    type = model.type,
                    notes = model.notes
                };

                db.tbl_session.Add(newSess);
                db.SaveChanges();

                message.Status = 1;
                message.Message = "Session Started.";
                message.Data = new { sessionid = newSess.id };
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = "Error: " + ex.Message;
            }

            return Json(message);
        }

        public class SessionEndModel
        {
            public int sessionid { get; set; }
            public string end_time { get; set; }
            public string start_lat { get; set; }
            public string start_lon { get; set; }
            public string end_lat { get; set; }
            public string end_lon { get; set; }
            public float distance { get; set; }
            public int capture_frequency { get; set; }
            public string? start_address { get; set; }
            public string? end_address { get; set; }
        }

        [HttpPost]
        [AllowAnonymous]
        public JsonResult end_session([FromBody] SessionEndModel model)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();

            try
            {
                var existingSession = db.tbl_session.FirstOrDefault(u => u.id == model.sessionid);

                if (existingSession != null)
                {
                    existingSession.start_lat = float.TryParse(model.start_lat, out var latVal) ? latVal : (float?)null;
                    existingSession.start_lon = float.TryParse(model.start_lon, out var lonVal) ? lonVal : (float?)null;
                    existingSession.end_lat = float.TryParse(model.end_lat, out var latVal1) ? latVal1 : (float?)null;
                    existingSession.end_lon = float.TryParse(model.end_lon, out var lonVal1) ? lonVal1 : (float?)null;
                    existingSession.end_time = DateTime.TryParse(model.end_time, out var ts) ? ts : (DateTime?)null;
                    existingSession.start_address = model.start_address;
                    existingSession.end_address = model.end_address;
                    existingSession.capture_frequency = model.capture_frequency;
                    existingSession.distance = model.distance;

                    db.SaveChanges();
                }

                message.Status = 1;
                message.Message = "Session Ended.";
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = "Error: " + ex.Message;
            }

            return Json(message);
        }

        // ==================== Polygons: list existing (unchanged) ====================

        [HttpGet]
        [Route("GetProjectPolygons")]
        public JsonResult GetProjectPolygons(int projectId) 
        {
            var polygons = db.Set<SignalTracker.Models.PolygonDto>()
                .FromSqlRaw(@"
                    SELECT id, name, ST_AsText(region) as wkt 
                    FROM map_regions 
                    WHERE status = 1 and tbl_project_id = {0}", projectId)
                .ToList();

            var result = polygons.Select(p => new { p.id, p.name, p.wkt });
            return Json(result);
        }

        // ==================== NEW: Save polygon + attach logs ====================

       // ==================== NEW: Save polygon + attach logs ====================

// ==================== NEW: Save polygon + attach logs ====================

// ==================== NEW: Save polygon + attach logs ====================

// ==================== NEW: Save polygon + attach logs ====================

// ==================== NEW: Save polygon + attach logs ====================

public class SavePolygonRequest
{
    public int? ProjectId { get; set; }
    public string Name { get; set; } = default!;

    // any ONE of these for polygon shape
    public string? Wkt { get; set; }
    public string? GeoJson { get; set; }
    // [[lon,lat], ...]  (order: lon, lat)
    public List<List<double>>? Coordinates { get; set; }

    // FULL array to store
    public List<int> LogIds { get; set; } = new List<int>();
}

// Minimal GeoJSON POCOs
public class GeoJson
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("features")] public List<Feature> Features { get; set; }
}
public class Feature
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("geometry")] public Geometry Geometry { get; set; }
    [JsonProperty("properties")] public Dictionary<string, object> Properties { get; set; }
}
public class Geometry
{
    [JsonProperty("type")] public string Type { get; set; }
    // [ring][vertex][lon/lat]
    [JsonProperty("coordinates")] public List<List<List<double>>> Coordinates { get; set; }
}

[HttpPost]
[Route("SavePolygon")]
[Consumes("application/json")]
public IActionResult SavePolygon([FromBody] SavePolygonRequest dto)
{
    if (dto == null || string.IsNullOrWhiteSpace(dto.Name))
        return BadRequest(new { Status = 0, Message = "Invalid payload: Name is required." });

    if (dto.LogIds == null || dto.LogIds.Count == 0)
        return BadRequest(new { Status = 0, Message = "LogIds is required and must contain at least one id." });

    try
    {
        // ---- Build WKT from any of the inputs ----
        string? wkt = dto.Wkt;

        // (A) Coordinates -> WKT (lon, lat)
        if (string.IsNullOrWhiteSpace(wkt) && dto.Coordinates != null && dto.Coordinates.Count >= 3)
        {
            var ring = new List<string>();
            foreach (var c in dto.Coordinates)
            {
                if (c.Count < 2) continue;
                ring.Add($"{c[0]} {c[1]}"); // lon lat
            }
            if (ring.Count < 3)
                return BadRequest(new { Status = 0, Message = "At least three coordinates required" });
            if (ring[0] != ring[^1]) ring.Add(ring[0]); // close ring
            wkt = $"POLYGON(({string.Join(", ", ring)}))";
        }

        // (B) GeoJSON -> WKT
        if (string.IsNullOrWhiteSpace(wkt) && !string.IsNullOrWhiteSpace(dto.GeoJson))
        {
            var gj = JsonConvert.DeserializeObject<GeoJson>(dto.GeoJson);
            var poly = gj?.Features?.FirstOrDefault(f =>
                f?.Geometry?.Type?.Equals("Polygon", StringComparison.OrdinalIgnoreCase) == true);

            if (poly == null) return BadRequest(new { Status = 0, Message = "No polygon in GeoJSON" });

            var ring = poly.Geometry.Coordinates?.FirstOrDefault();
            if (ring == null || ring.Count < 3)
                return BadRequest(new { Status = 0, Message = "Invalid polygon coordinates" });

            var first = ring[0]; var last = ring[^1];
            if (first[0] != last[0] || first[1] != last[1]) ring.Add(first);

            var coordsText = string.Join(", ", ring.Select(c => $"{c[0]} {c[1]}")); // lon lat
            wkt = $"POLYGON(({coordsText}))";
        }

        if (string.IsNullOrWhiteSpace(wkt))
            return BadRequest(new { Status = 0, Message = "Provide polygon as Coordinates / Wkt / GeoJson" });

        // ---- Prepare LogIds ----
        var ids = dto.LogIds.Distinct().Where(i => i > 0).ToList();
        if (ids.Count == 0)
            return BadRequest(new { Status = 0, Message = "LogIds must contain valid positive ids." });

        // ---- Open connection once ----
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            conn.Open();

        // ---- Detect columns + nullability + data types (no DbSet needed) ----
        bool hasRegion = false, hasWktCol = false, hasLogIdCol = false, regionNullable = true;
        string? logIdDataType = null;
        bool hasLogIdsJsonCol = false;

        using (var cmdCols = conn.CreateCommand())
        {
            cmdCols.CommandText = @"
                SELECT COLUMN_NAME, IS_NULLABLE, DATA_TYPE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'tbl_savepolygon'";
            using var rdr = cmdCols.ExecuteReader();
            while (rdr.Read())
            {
                var col = rdr.GetString(0);
                var isNull = rdr.GetString(1); // 'YES'/'NO'
                var dtype  = rdr.GetString(2).ToLowerInvariant();

                if (col.Equals("region", StringComparison.OrdinalIgnoreCase))
                {
                    hasRegion = true;
                    regionNullable = string.Equals(isNull, "YES", StringComparison.OrdinalIgnoreCase);
                }
                else if (col.Equals("wkt", StringComparison.OrdinalIgnoreCase))
                {
                    hasWktCol = true;
                }
                else if (col.Equals("logid", StringComparison.OrdinalIgnoreCase))
                {
                    hasLogIdCol = true;
                    logIdDataType = dtype; // e.g., bigint, json, text, varchar
                }
                else if (col.Equals("logids_json", StringComparison.OrdinalIgnoreCase))
                {
                    hasLogIdsJsonCol = true;
                }
            }
        }

        if (!hasWktCol && !hasRegion)
            return StatusCode(500, new { Status = 0, Message = "Table must have either 'wkt' or 'region' to store polygon geometry." });

        // ---- Ensure we have a column to store the ARRAY ----
        // Preferred: use 'logid' itself as JSON/TEXT. If it's numeric, try to ALTER it.
        string targetArrayColumn = "logid";  // by default we will write array into logid
        string? schemaNote = null;

        bool logidIsNumeric = logIdDataType != null &&
            new[] { "int", "bigint", "smallint", "mediumint", "tinyint", "integer", "decimal", "double", "float" }
            .Contains(logIdDataType);

        if (!hasLogIdCol)
        {
            // no logid at all -> try to add logids_json JSON
            try
            {
                using var cmdAdd = conn.CreateCommand();
                cmdAdd.CommandText = "ALTER TABLE tbl_savepolygon ADD COLUMN logids_json JSON NULL";
                cmdAdd.ExecuteNonQuery();
                hasLogIdsJsonCol = true;
                targetArrayColumn = "logids_json";
                schemaNote = "Added column logids_json(JSON).";
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Status = 0, Message = "No 'logid' column and couldn't add 'logids_json' JSON column.", Error = ex.Message });
            }
        }
        else if (logidIsNumeric)
        {
            // Try to convert logid to JSON (preferred), fallback LONGTEXT; if both fail, use/add logids_json
            bool converted = false;
            try
            {
                using var cmdAlter = conn.CreateCommand();
                cmdAlter.CommandText = "ALTER TABLE tbl_savepolygon MODIFY COLUMN logid JSON NULL";
                cmdAlter.ExecuteNonQuery();
                converted = true;
                logIdDataType = "json";
                schemaNote = "Converted logid to JSON.";
            }
            catch
            {
                try
                {
                    using var cmdAlter = conn.CreateCommand();
                    cmdAlter.CommandText = "ALTER TABLE tbl_savepolygon MODIFY COLUMN logid LONGTEXT NULL";
                    cmdAlter.ExecuteNonQuery();
                    converted = true;
                    logIdDataType = "longtext";
                    schemaNote = "Converted logid to LONGTEXT.";
                }
                catch
                {
                    // last fallback: use/add logids_json
                    if (!hasLogIdsJsonCol)
                    {
                        try
                        {
                            using var cmdAdd = conn.CreateCommand();
                            cmdAdd.CommandText = "ALTER TABLE tbl_savepolygon ADD COLUMN logids_json JSON NULL";
                            cmdAdd.ExecuteNonQuery();
                            hasLogIdsJsonCol = true;
                            targetArrayColumn = "logids_json";
                            schemaNote = "Added column logids_json(JSON) (no permission to alter logid).";
                        }
                        catch (Exception ex2)
                        {
                            return StatusCode(500, new
                            {
                                Status = 0,
                                Message = "Column 'logid' is numeric and couldn't be altered; also failed to add 'logids_json'.",
                                Error = ex2.Message
                            });
                        }
                    }
                    else
                    {
                        targetArrayColumn = "logids_json";
                        schemaNote = "Using existing logids_json(JSON) (logid is numeric).";
                    }
                }
            }
        }
        else
        {
            // logid exists and is JSON/TEXT/VARCHAR already -> good
            targetArrayColumn = "logid";
        }

        // ---- Build JSON array string for LogIds ----
        var logIdJson = JsonConvert.SerializeObject(ids);  // "[101,102,103]"

        // ---- Build INSERT dynamically (single row) ----
        var columns = new List<string> { "name" };
        var values  = new List<string> { "{0}" };   // dto.Name

        if (hasWktCol) { columns.Add("wkt"); values.Add("{1}"); }

        if (hasRegion)
        {
            columns.Add("region");
            values.Add(regionNullable ? "NULL" : "ST_GeomFromText({1}, 4326)");
        }

        // ensure the target array column is included
        columns.Add(targetArrayColumn);
        values.Add("{2}");

        string insertSql = $"INSERT INTO tbl_savepolygon ({string.Join(", ", columns)}) " +
                           $"VALUES ({string.Join(", ", values)});";

        long polygonId;

        using var tx = db.Database.BeginTransaction();

        // 1) Insert single row
        db.Database.ExecuteSqlRaw(insertSql, dto.Name, wkt, logIdJson);

        // 2) Get id on same connection+tx
        using (var cmdId = conn.CreateCommand())
        {
            cmdId.CommandText = "SELECT LAST_INSERT_ID()";
            cmdId.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            var obj = cmdId.ExecuteScalar();
            polygonId = (obj is long l) ? l : Convert.ToInt64(obj);
        }

        if (polygonId <= 0)
        {
            tx.Rollback();
            return StatusCode(500, new { Status = 0, Message = "Polygon inserted but id not retrieved." });
        }

        tx.Commit();

        return Ok(new
        {
            Status = 1,
            Message = $"Polygon saved; full LogIds array stored in '{targetArrayColumn}'.",
            SchemaNote = schemaNote,
            PolygonId = polygonId,
            Name = dto.Name,
            Wkt = wkt,
            LogIdsStored = ids,
            ProjectId = dto.ProjectId
        });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { Status = 0, Message = "Error: " + ex.Message });
    }
}



        // ==================== Polygon analytics (unchanged) ====================

        [HttpGet]
        [Route("GetPolygonLogCount")]
        public async Task<JsonResult> GetPolygonLogCount(int polygonId, DateTime? from = null, DateTime? to = null)
        {
            try
            {
                IQueryable<tbl_network_log> q = db.tbl_network_log.Where(l => l.polygon_id == polygonId);

                if (from.HasValue) q = q.Where(l => l.timestamp >= from.Value);
                if (to.HasValue) q = q.Where(l => l.timestamp < to.Value.AddDays(1)); // inclusive day

                var total = await q.CountAsync();
                DateTime? first = await q.MinAsync(l => l.timestamp);
                DateTime? last = await q.MaxAsync(l => l.timestamp);

                return Json(new { polygonId, total, from, to, first, last });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { message = "Server error: " + ex.Message }) { StatusCode = 500 };
            }
        }

        public class PolygonLogFilter
        {
            public int PolygonId { get; set; }
            public DateTime? From { get; set; }
            public DateTime? To { get; set; }
            public int Limit { get; set; } = 20000;
        }

        


        // ==================== Other existing endpoints (unchanged) ====================

        public class NetworkLogFilters
        {
            public int SessionId { get; set; }
            public int? projectId { get; set; }
            public string token { get; set; }
            public DateTime? fromDate { get; set; }
            public DateTime? toDate { get; set; }
            public string providers { get; set; }
            public string technology { get; set; }
            public string metric { get; set; }
            public bool isBestTechnology { get; set; }
            public string Band { get; set; }
            public string EARFCN { get; set; }
            public string State { get; set; }
            public bool loadFilters { get; set; } = false;
        }

        public class MapFilter
        {
            public int session_id { get; set; }
            public string? NetworkType { get; set; }
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public int page { get; set; } = 1;
            public int limit { get; set; } = 1000;
        }

        [HttpGet]
        [Route("GetNetworkLog")]
        public async Task<JsonResult> GetNetworkLog([FromQuery] MapFilter filters)
        {
            if (filters.session_id <= 0)
            {
                return Json(new List<object>());
            }

            try
            {
                IQueryable<tbl_network_log> query = db.tbl_network_log
                                                      .Where(log => log.session_id == filters.session_id);

                if (!string.IsNullOrEmpty(filters.NetworkType) && filters.NetworkType.ToUpper() != "ALL")
                {
                    query = query.Where(log => log.network == filters.NetworkType);
                }
                if (filters.StartDate.HasValue)
                {
                    query = query.Where(log => log.timestamp >= filters.StartDate.Value);
                }
                if (filters.EndDate.HasValue)
                {
                    var endDate = filters.EndDate.Value.AddDays(1);
                    query = query.Where(log => log.timestamp < endDate);
                }

                var paginatedQuery = query
                    .OrderBy(log => log.timestamp)
                    .Skip((filters.page - 1) * filters.limit)
                    .Take(filters.limit);

                var logs = await paginatedQuery
                    .Select(log => new
                    {
                        log.session_id,
                        log.lat,
                        log.lon,
                        log.rsrp,
                        log.rsrq,
                        log.sinr,
                        log.network,
                        log.band,
                        log.timestamp,
                        log.dl_tpt,
                        log.ul_tpt,
                        log.m_alpha_long,
                        log.mos,
                        log.volte_call
                    })
                    .ToListAsync();

                return Json(logs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetNetworkLog: {ex.Message}");
                return new JsonResult(new { message = "An error occurred on the server." }) { StatusCode = 500 };
            }
        }

   

[HttpPost]
[Route("GetPredictionLog")]
[Consumes("application/json")]
public JsonResult GetPredictionLog([FromBody] PredictionLogQuery q)
{
    var message = new ReturnAPIResponse();

    try
    {
        cf.SessionCheck();

        // ---------- (0) Load / Save coverage-hole stuff ----------
        string? coverageHoleRaw = null;                 // ranges json raw
        double? coverageHoleValue = null;               // single numeric

        // Find or create thresholds row
        var th = db.thresholds.FirstOrDefault(x => x.user_id == cf.UserId);
        if (th == null)
        {
            th = new thresholds { user_id = cf.UserId, is_default = 0 };
            db.thresholds.Add(th);
            db.SaveChanges();
        }

        // Save ranges JSON if sent
        if (q.coverageHoleJson.HasValue)
        {
            coverageHoleRaw = q.coverageHoleJson.Value.GetRawText();
            th.coveragehole_json = coverageHoleRaw;
        }

        // Save single value if sent
        if (q.coverageHole.HasValue)
        {
            coverageHoleValue = q.coverageHole.Value;
            th.coveragehole_value = coverageHoleValue;
        }

        // If nothing sent, load existing
        if (!q.coverageHoleJson.HasValue)
            coverageHoleRaw = th.coveragehole_json;

        if (!q.coverageHole.HasValue)
            coverageHoleValue = th.coveragehole_value;

        // Persist if we changed anything
        if (q.coverageHoleJson.HasValue || q.coverageHole.HasValue)
            db.SaveChanges();

        // Parse ranges JSON to typed list (optional for UI)
        List<SettingReangeColor>? coverageHoleSetting = null;
        if (!string.IsNullOrWhiteSpace(coverageHoleRaw))
        {
            try
            {
                coverageHoleSetting =
                    Newtonsoft.Json.JsonConvert.DeserializeObject<List<SettingReangeColor>>(coverageHoleRaw!);
            }
            catch { /* ignore bad json, still return raw */ }
        }

        // ---------- (1) Base query + filters ----------
        IQueryable<tbl_prediction_data> query = db.tbl_prediction_data;
        message.Status = 1;

        if (q.projectId.HasValue && q.projectId.Value != 0)
            query = query.Where(e => e.tbl_project_id == q.projectId.Value);

        if (!string.IsNullOrEmpty(q.Band))
            query = query.Where(e => e.band == q.Band);

        if (!string.IsNullOrEmpty(q.EARFCN))
            query = query.Where(e => e.earfcn == q.EARFCN);

        if (q.fromDate.HasValue) query = query.Where(e => e.timestamp >= q.fromDate.Value);
        if (q.toDate.HasValue)   query = query.Where(e => e.timestamp <  q.toDate.Value.AddDays(1));

        // ---------- (2) Metric selection ----------
        var metricKey = (q.metric ?? "RSRP").Trim().ToUpperInvariant();

        var data = query.Select(a => new
        {
            a.lat,
            a.lon,
            prm = metricKey == "RSRP" ? a.rsrp :
                  (metricKey == "RSRQ" ? a.rsrq : a.sinr)
        }).ToList();

        double? averageRsrp = query.Where(x => x.rsrp != null).Average(x => (double?)x.rsrp);
        double? averageRsrq = query.Where(x => x.rsrq != null).Average(x => (double?)x.rsrq);
        double? averageSinr = query.Where(x => x.sinr != null).Average(x => (double?)x.sinr);

        // ---------- (3) Metric color thresholds ----------
        GraphStruct CoveragePerfGraph = new GraphStruct();
        var setting = db.thresholds.FirstOrDefault(x => x.user_id == cf.UserId)
                   ?? db.thresholds.FirstOrDefault(x => x.is_default == 1);

        List<SettingReangeColor>? colorSetting = null;

        if (setting != null && data.Count > 0)
        {
            if (metricKey == "RSRP")
                colorSetting = Newtonsoft.Json.JsonConvert.DeserializeObject<List<SettingReangeColor>>(setting.rsrp_json);
            else if (metricKey == "RSRQ")
                colorSetting = Newtonsoft.Json.JsonConvert.DeserializeObject<List<SettingReangeColor>>(setting.rsrq_json);
            else if (metricKey == "SINR" || metricKey == "SNR")
                colorSetting = Newtonsoft.Json.JsonConvert.DeserializeObject<List<SettingReangeColor>>(setting.sinr_json);

            if (colorSetting != null && colorSetting.Count > 0)
            {
                int total = data.Count;
                var series = new GrapSeries();
                foreach (var s in colorSetting)
                {
                    CoveragePerfGraph.Category.Add(s.range);
                    int matched = data.Count(a => a.prm >= s.min && a.prm <= s.max);
                    float per = total > 0 ? (matched * 100f / total) : 0f;
                    series.data.Add(new { y = Math.Round(per, 2), color = s.color });
                }
                CoveragePerfGraph.series.Add(series);
            }
        }

        // ---------- (4) Inside-polygons optional ----------
        if (q.pointsInsideBuilding == 1 && q.projectId.HasValue)
        {
            const string sqlQuery = @"
                SELECT
                    tpd.tbl_project_id,
                    tpd.lat,
                    tpd.lon,
                    tpd.rsrp,
                    tpd.rsrq,
                    tpd.sinr,
                    tpd.band,
                    tpd.earfcn
                FROM tbl_prediction_data AS tpd
                JOIN map_regions AS mr ON tpd.tbl_project_id = mr.tbl_project_id
                WHERE tpd.tbl_project_id = {0}
                  AND ST_Contains(
                        mr.region,
                        ST_PointFromText(CONCAT('POINT(', tpd.lon, ' ', tpd.lat, ')'), 4326)
                  );";

            var matching = db.Set<PredictionPointDto>().FromSqlRaw(sqlQuery, q.projectId.Value).ToList();

            var data1 = matching.Select(a => new
            {
                a.lat,
                a.lon,
                prm = metricKey == "RSRP" ? a.rsrp :
                      (metricKey == "RSRQ" ? a.rsrq : a.sinr)
            }).ToList();

            double? avgRsrp1 = matching.Where(x => x.rsrp != null).Average(x => (double?)x.rsrp);
            double? avgRsrq1 = matching.Where(x => x.rsrq != null).Average(x => (double?)x.rsrq);
            double? avgSinr1 = matching.Where(x => x.sinr != null).Average(x => (double?)x.sinr);

            message.Data = new
            {
                dataList = data1,
                avgRsrp = avgRsrp1,
                avgRsrq = avgRsrq1,
                avgSinr = avgSinr1,

                // ✅ yeh teen hamesha bhejo
                coverageHole = coverageHoleValue,          // single numeric
                coverageHoleSetting = coverageHoleSetting, // parsed ranges
                coverageHoleRaw = coverageHoleRaw,         // raw json string

                colorSetting = colorSetting,
                coveragePerfGraph = CoveragePerfGraph
            };
            return Json(message);
        }

        // ---------- (5) default response ----------
        message.Data = new
        {
            dataList = data,
            avgRsrp = averageRsrp,
            avgRsrq = averageRsrq,
            avgSinr = averageSinr,

            // ✅ yeh teen hamesha bhejo
            coverageHole = coverageHoleValue,
            coverageHoleSetting = coverageHoleSetting,
            coverageHoleRaw = coverageHoleRaw,

            colorSetting = colorSetting,
            coveragePerfGraph = CoveragePerfGraph
        };
    }
    catch (Exception ex)
    {
        message.Status = 0;
        message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
    }

    return Json(message);
}

        [HttpGet] // existing
        public JsonResult GetPredictionDataForSelectedBuildingPolygonsRaw(int projectId, string metric)
        {
            try
            {
                string sqlQuery = @"
                SELECT
                    tpd.tbl_project_id,
                    tpd.lat,
                    tpd.lon,
                    tpd.rsrp,
                    tpd.rsrq,
                    tpd.sinr,
                    tpd.band,
                    tpd.earfcn
                FROM
                    tbl_prediction_data AS tpd
                JOIN
                    map_regions AS mr ON tpd.tbl_project_id = mr.tbl_project_id
                WHERE
                    tpd.tbl_project_id = {0} AND
                    ST_Contains(mr.region, ST_PointFromText(CONCAT('POINT(', tpd.lon, ' ', tpd.lat, ')'), 4326));";

                var matchingPoints = db.Set<PredictionPointDto>()
                                       .FromSqlRaw(sqlQuery, projectId)
                                       .ToList();

                var data = matchingPoints.Select(a => new
                {
                    a.lat,
                    a.lon,
                    Prm = metric == "RSRP" ? a.rsrp : (metric == "RSRQ" ? a.rsrq : a.sinr)
                }).ToList();

                return Json(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching prediction data with raw SQL: {ex.Message}");
                return Json(new { error = "An error occurred while fetching data.", details = ex.Message });
            }
        }

        [HttpGet]
        [Route("GetProjects")]
        public JsonResult GetProjects()
        {
            ReturnAPIResponse message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message.Status = 1;

                message.Data = db.tbl_project.Select(a => new
                {
                    a.id,
                    a.project_name,
                    a.ref_session_id,
                    a.from_date,
                    a.to_date,
                    a.provider,
                    a.tech,
                    a.band,   
                    a.earfcn,
                    a.apps,
                    a.created_on
                }).ToList();
            }
            catch (Exception ex)
            {
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> UploadImage([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif" };
            if (!allowedTypes.Contains(file.ContentType))
            {
                return BadRequest("Only image files are allowed.");
            }

            var webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadFolder = Path.Combine(webRootPath, "uploaded_images");

            if (!Directory.Exists(uploadFolder))
                Directory.CreateDirectory(uploadFolder);

            var filePath = Path.Combine(uploadFolder, Path.GetFileName(file.FileName));

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Ok(new { message = "Image uploaded successfully.", filename = file.FileName });
        }

        public class LogFilterModel
        {
            public DateTime? StartDate { get; set; }
            public DateTime? EndDate { get; set; }
            public string? Provider { get; set; }
            public int? PolygonId { get; set; }
        }

        [HttpGet]
        [Route("GetLogsByDateRange")]
        public async Task<JsonResult> GetLogsByDateRange([FromQuery] LogFilterModel filters)
        {
            try
            {
                IQueryable<tbl_network_log> query = db.tbl_network_log;

                if (filters.StartDate.HasValue)
                {
                    query = query.Where(log => log.timestamp >= filters.StartDate.Value);
                }

                if (filters.EndDate.HasValue)
                {
                    var endDate = filters.EndDate.Value.AddDays(1);
                    query = query.Where(log => log.timestamp < endDate);
                }

                if (!string.IsNullOrEmpty(filters.Provider))
                {
                    query = query.Where(log => log.m_alpha_long == filters.Provider);
                }

                if (filters.PolygonId.HasValue)
                    query = query.Where(log => log.polygon_id == filters.PolygonId.Value);

                var logs = await query
                    .OrderBy(log => log.timestamp)
                    .Take(20000)
                    .Select(log => new
                    {
                        id = log.id,
                        log.session_id,
                        log.lat,
                        log.lon,
                        log.rsrp,
                        log.rsrq,
                        log.sinr,
                        log.network,
                        log.band,
                        log.timestamp,
                        provider = log.m_alpha_long,
                        log.dl_tpt,
                        log.ul_tpt,
                        log.mos,
                        log.polygon_id
                    })
                    .ToListAsync();

                if (!logs.Any())
                {
                    return Json(new List<object>());
                }

                return Json(logs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLogsByDateRange: {ex.Message}");
                return new JsonResult(new { message = "An error occurred on the server." }) { StatusCode = 500 };
            }
        }

        public class NetworkLogPostModel
        {
            [JsonPropertyName("sessionid")]
            public int sessionid { get; set; }

            [JsonPropertyName("data")]
            public List<log_network> data { get; set; }
        }

        [HttpGet]
        [Route("GetProviders")]
        public JsonResult GetProviders()
        {
            var providerNames = db.tbl_network_log
                .Where(p => !string.IsNullOrEmpty(p.m_alpha_long))
                .Select(p => p.m_alpha_long)
                .Distinct()
                .ToList();

            var providers = providerNames
                .Select((name, index) => new { id = index + 1, name })
                .ToList();

            return Json(providers);
        }

        [HttpGet]
        [Route("GetTechnologies")]
        public JsonResult GetTechnologies()
        {
            var technologyNames = db.tbl_network_log
                .Where(t => !string.IsNullOrEmpty(t.network))
                .Select(t => t.network)
                .Distinct()
                .ToList();

            var technologies = technologyNames
                .Select((name, index) => new { id = name, name })
                .ToList();

            return Json(technologies);
        }

        [HttpGet]
        [Route("GetBands")]
        public JsonResult GetBands()
        {
            try
            {
                var bandNames = db.tbl_network_log
                    .Where(b => !string.IsNullOrEmpty(b.band))
                    .Select(b => b.band)
                    .Distinct()
                    .ToList();

                var bands = bandNames
                    .Select((name, index) => new { id = index + 1, name })
                    .ToList();

                return Json(bands);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetBands: {ex.Message}");
                return new JsonResult(new
                {
                    status = 0,
                    message = "Error fetching bands data",
                    error = ex.Message
                })
                { StatusCode = 500 };
            }
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<JsonResult> log_networkAsync([FromBody] NetworkLogPostModel model)
        {
            ReturnMessage message = new ReturnMessage();

            try
            {
                if (model != null && model.data != null && model.data.Any())
                {
                    foreach (var item in model.data)
                    {
                        var log = new tbl_network_log
                        {
                            session_id = model.sessionid,
                            timestamp = DateTime.TryParse(item.timestamp, out var ts) ? ts : (DateTime?)null,
                            lat = float.TryParse(item.lat, out var latVal) ? latVal : (float?)null,
                            lon = float.TryParse(item.lon, out var lonVal) ? lonVal : (float?)null,
                            battery = int.TryParse(item.battery, out var batVal) ? batVal : (int?)null,
                            dls = item.dls,
                            uls = item.uls,
                            call_state = item.call_state,
                            hotspot = item.hotspot,
                            apps = item.apps,
                            num_cells = int.TryParse(item.num_cells, out var ncVal) ? ncVal : (int?)null,
                            network = item.network,
                            m_mcc = int.TryParse(item.m_mcc, out var mccVal) ? mccVal : (int?)null,
                            m_mnc = int.TryParse(item.m_mnc, out var mncVal) ? mncVal : (int?)null,
                            m_alpha_long = item.m_alpha_long,
                            m_alpha_short = item.m_alpha_short,
                            mci = item.mci,
                            pci = item.pci,
                            tac = item.tac,
                            earfcn = item.earfcn,
                            rssi = float.TryParse(item.rssi, out var rssiVal) ? rssiVal : (float?)null,
                            rsrp = float.TryParse(item.rsrp, out var rsrpVal) ? rsrpVal : (float?)null,
                            rsrq = float.TryParse(item.rsrq, out var rsrqVal) ? rsrqVal : (float?)null,
                            sinr = float.TryParse(item.sinr, out var sinrVal) ? sinrVal : (float?)null,
                            total_rx_kb = item.total_rx_kb,
                            total_tx_kb = item.total_tx_kb,
                            mos = float.TryParse(item.mos, out var mosVal) ? mosVal : (float?)null,
                            jitter = float.TryParse(item.jitter, out var jitterVal) ? jitterVal : (float?)null,
                            latency = float.TryParse(item.latency, out var latnVal) ? latnVal : (float?)null,
                            packet_loss = float.TryParse(item.packet_loss, out var lossVal) ? lossVal : (float?)null,
                            dl_tpt = item.dl_tpt,
                            ul_tpt = item.ul_tpt,
                            volte_call = item.volte_call,
                            band = item.band,
                            cqi = float.TryParse(item.cqi, out var cqiVal) ? cqiVal : (float?)null,
                            bler = item.bler,
                            primary_cell_info_1 = item.primary_cell_info_1,
                            primary_cell_info_2 = item.primary_cell_info_2,
                            all_neigbor_cell_info = item.all_neigbor_cell_info,
                            image_path = item.image_path,
                        };

                        try
                        {
                            var lat = log.lat;
                            var lon = log.lon;
                            string pointWKT = $"POINT({lon} {lat})";
                            int srid = 4326;

                            var polygonId = db.PolygonMatches
                                .FromSqlRaw(@"
                                   SELECT id FROM map_regions 
                                   WHERE ST_Contains(region, ST_GeomFromText({0}, {1})) 
                                   LIMIT 1", pointWKT, srid)
                                .Select(p => (int?)p.id)
                                .FirstOrDefault();

                            log.polygon_id = polygonId;
                        }
                        catch { /* ignore and keep saving the log */ }

                        db.tbl_network_log.Add(log);
                    }

                    db.SaveChanges();
                    message.Status = 1;
                    message.Message = "Data saved successfully.";
                }
                else
                {
                    message.Status = 0;
                    message.Message = "No data received.";
                }
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = "Error: " + ex.Message;
            }

            return Json(message);
        }
    }
}
