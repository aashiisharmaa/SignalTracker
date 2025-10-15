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

public class SavePolygonRequest
{
    public int? ProjectId { get; set; }                // optional (not stored in tbl_savepolygon)
    public string Name { get; set; } = default!;

    // any one of these for polygon shape
    public string? Wkt { get; set; }
    public string? GeoJson { get; set; }

    // [[lon,lat], ...]
    public List<List<double>>? Coordinates { get; set; }

    // REQUIRED: insert one row per LogId in tbl_savepolygon
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

        // ---- Detect table columns (region / wkt) WITHOUT DbSet ----
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn2 = db.Database.GetDbConnection();
        if (conn2.State != System.Data.ConnectionState.Open)
            conn2.Open();

        using (var cmd2 = conn2.CreateCommand())
        {
            cmd2.CommandText = @"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'tbl_savepolygon'";

            if (db.Database.CurrentTransaction != null)
                cmd2.Transaction = db.Database.CurrentTransaction.GetDbTransaction();

            using var rdr = cmd2.ExecuteReader();
            while (rdr.Read())
                cols.Add(rdr.GetString(0));
        }

        bool hasRegion = cols.Contains("region");
        bool hasWktCol = cols.Contains("wkt");

        if (!hasRegion && !hasWktCol)
            return StatusCode(500, new { Status = 0, Message = "tbl_savepolygon must have either 'region' (POLYGON) or 'wkt' (TEXT) column." });

        // ---- Choose INSERT shape that matches the table ----
        string insertSql =
            (hasRegion && hasWktCol)
                ? @"INSERT INTO tbl_savepolygon (name, region, wkt, logid)
                    VALUES ({0}, ST_GeomFromText({1}, 4326), {1}, {2});"
                : (hasRegion)
                    ? @"INSERT INTO tbl_savepolygon (name, region, logid)
                        VALUES ({0}, ST_GeomFromText({1}, 4326), {2});"
                    : @"INSERT INTO tbl_savepolygon (name, wkt, logid)
                        VALUES ({0}, {1}, {2});";

        var inserted = new List<long>();

        // ---- Single transaction for all inserts ----
        using var tx = db.Database.BeginTransaction();

        // open the same connection once
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            conn.Open();

        foreach (var logId in ids)
        {
            // insert
            db.Database.ExecuteSqlRaw(insertSql, dto.Name, wkt, logId);

            // fetch LAST_INSERT_ID() on the same connection + transaction
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT LAST_INSERT_ID()";
            if (db.Database.CurrentTransaction != null)
                cmd.Transaction = db.Database.CurrentTransaction.GetDbTransaction();

            var obj = cmd.ExecuteScalar();
            var newId = (obj is long l) ? l : Convert.ToInt64(obj);

            if (newId <= 0)
            {
                tx.Rollback();
                return StatusCode(500, new { Status = 0, Message = "Row saved but id not retrieved." });
            }

            inserted.Add(newId);
        }

        tx.Commit();

        return Ok(new
        {
            Status = 1,
            Message = "Polygon saved in tbl_savepolygon for each specified LogId.",
            Name = dto.Name,
            Wkt = wkt,
            InsertedCount = inserted.Count,
            InsertedIds = inserted,
            LogIds = ids,
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

        [HttpGet]
        [Route("GetPolygonLogs")]
        public async Task<JsonResult> GetPolygonLogs([FromQuery] PolygonLogFilter f)
        {
            if (f.PolygonId <= 0) return Json(new List<object>());
            try
            {
                IQueryable<tbl_network_log> q = db.tbl_network_log.Where(l => l.polygon_id == f.PolygonId);
                if (f.From.HasValue) q = q.Where(l => l.timestamp >= f.From.Value);
                if (f.To.HasValue) q = q.Where(l => l.timestamp < f.To.Value.AddDays(1));

                var logs = await q.OrderBy(l => l.timestamp)
                                  .Take(Math.Max(1, Math.Min(f.Limit, 20000)))
                                  .Select(l => new
                                  {
                                      l.session_id, l.lat, l.lon, l.timestamp,
                                      l.network, l.band, l.dl_tpt, l.ul_tpt, l.m_alpha_long,
                                      l.rsrp, l.rsrq, l.sinr, l.mos, l.polygon_id
                                  })
                                  .ToListAsync();

                return Json(logs);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { message = "Server error: " + ex.Message }) { StatusCode = 500 };
            }
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

        [HttpGet]
        [Route("GetPredictionLog")]
        public JsonResult GetPredictionLog(int? projectId, string token, DateTime? fromDate, DateTime? toDate,
                                   string providers, string technology, string metric,
                                   bool isBestTechnology, string Band, string EARFCN, string State, int pointsInsideBuilding = 0, bool loadFilters = false)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();

            try
            {
                cf.SessionCheck();

                IQueryable<tbl_prediction_data> query = db.tbl_prediction_data;

                message.Status = 1;

                if (projectId.HasValue && projectId != 0)
                {
                    query = query.Where(e => e.tbl_project_id == projectId);
                }

                if (!string.IsNullOrEmpty(Band))
                    query = query.Where(e => e.band == Band);

                if (!string.IsNullOrEmpty(EARFCN))
                    query = query.Where(e => e.earfcn == EARFCN);

                var data = query.Select(a => new
                {
                    a.lat,
                    a.lon,
                    prm = metric == "RSRP" ? a.rsrp : (metric == "RSRQ" ? a.rsrq : a.sinr)
                }).ToList();

                double? averageRsrp = query.Where(x => x.rsrp != null).Average(x => (double?)x.rsrp);
                double? averageRsrq = query.Where(x => x.rsrq != null).Average(x => (double?)x.rsrq);
                double? averageSinr = query.Where(x => x.sinr != null).Average(x => (double?)x.sinr);

                GraphStruct CoveragePerfGraph = new GraphStruct();
                var setting = db.thresholds.FirstOrDefault(x => x.user_id == cf.UserId);
                if (setting == null)
                    setting = db.thresholds.FirstOrDefault(x => x.is_default == 1);

                List<SettingReangeColor>? settingObj = null;
                if (setting != null && data.Count() > 0)
                {
                    if (metric == "RSRP")
                        settingObj = JsonConvert.DeserializeObject<List<SettingReangeColor>>(setting.rsrp_json);
                    else if (metric == "RSRQ")
                        settingObj = JsonConvert.DeserializeObject<List<SettingReangeColor>>(setting.rsrq_json);
                    else if (metric == "SNR")
                        settingObj = JsonConvert.DeserializeObject<List<SettingReangeColor>>(setting.sinr_json);

                    if (settingObj != null && settingObj.Count > 0)
                    {
                        int totalCount = data.Count();
                        GrapSeries seriesObj = new GrapSeries();
                        foreach (var s in settingObj)
                        {
                            CoveragePerfGraph.Category.Add(s.range);
                            int matchedCount = data.Count(a => a.prm >= s.min && a.prm <= s.max);
                            float per = totalCount > 0 ? (matchedCount * 100f / totalCount) : 0f;
                            seriesObj.data.Add(new { y = Math.Round(per, 2), color = s.color });
                        }
                        CoveragePerfGraph.series.Add(seriesObj);
                    }
                }

                if (pointsInsideBuilding == 1)
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

                        var data1 = matchingPoints.Select(a => new
                        {
                            a.lat,
                            a.lon,
                            prm = metric == "RSRP" ? a.rsrp : (metric == "RSRQ" ? a.rsrq : a.sinr)
                        }).ToList();

                        double? averageRsrp1 = matchingPoints.Where(x => x.rsrp != null).Average(x => (double?)x.rsrp);
                        double? averageRsrq1 = matchingPoints.Where(x => x.rsrq != null).Average(x => (double?)x.rsrq);
                        double? averageSinr1 = matchingPoints.Where(x => x.sinr != null).Average(x => (double?)x.sinr);

                        message.Data = new
                        {
                            dataList = data1,
                            avgRsrp = averageRsrp1,
                            avgRsrq = averageRsrq1,
                            avgSinr = averageSinr1,
                            colorSetting = settingObj,
                            coveragePerfGraph = CoveragePerfGraph,
                        };

                        return Json(message);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error fetching prediction data with raw SQL: {ex.Message}");
                        return Json(new { error = "An error occurred while fetching data.", details = ex.Message });
                    }
                }

                message.Data = new
                {
                    dataList = data,
                    avgRsrp = averageRsrp,
                    avgRsrq = averageRsrq,
                    avgSinr = averageSinr,
                    colorSetting = settingObj,
                    coveragePerfGraph = CoveragePerfGraph,
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
