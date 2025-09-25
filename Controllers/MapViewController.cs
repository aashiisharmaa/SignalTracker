using Microsoft.AspNetCore.Mvc;
using SignalTracker.Models; // Your DbContext model
using System.Linq;
using System.Threading.Tasks;
using SignalTracker.Helper;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using System.Text.Json.Serialization;
using MySqlConnector;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Humanizer;
using System.Linq;
using System;
using Newtonsoft.Json;

using System.Drawing;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Linq;


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
                //var existingUser = db.tbl_user.FirstOrDefault(u => u.mobile == model.mobile);
                if (model != null && model.device_id != null)
                {
                    var existingUser1 = db.tbl_user.FirstOrDefault(u => u.device_id == model.device_id);
                    if (existingUser1 != null)
                    {
                        message.Status = 1;
                        message.Message = "This device is already registered as - " + existingUser1.name;
                        message.Data = new { userid = existingUser1.id }; // Replace 'id' with your PK column
                    }
                }
                var existingUser = db.tbl_user.FirstOrDefault(u => u.mobile == model.mobile && u.make == model.make);

                if (existingUser != null)
                {
                    message.Status = 1;
                    message.Message = "User already exists.";
                    message.Data = new { userid = existingUser.id }; // Replace 'id' with your PK column
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
                    message.Data = new { userid = newUser.id }; // Replace 'id' with your PK column
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
                message.Data = new { sessionid = newSess.id }; // Replace 'id' with your PK column

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
                    existingSession.end_lon = float.TryParse(model.end_lon, out var lonVal1) ? latVal1 : (float?)null;
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

        [HttpGet]
        [Route("GetProjectPolygons")]
        public JsonResult GetProjectPolygons(int projectId)
        {
            var polygons = db.Set<PolygonDto>()
             .FromSqlRaw(@"
                SELECT id, name, ST_AsText(region) as wkt 
                FROM map_regions 
                WHERE status = 1 and tbl_project_id = {0}", projectId)
             .ToList();

            var result = polygons.Select(p => new
            {
                p.id,
                p.name,
                p.wkt
            });

            /*var result = polygons.Select(p => new {
                p.id,
                p.name,
                coordinates = ParsePolygonWKT(p.wkt)
            });*/

            return Json(result);
        }


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
        [Route("GetNetworkLog")] // Explicitly define the route for clarity
        public async Task<JsonResult> GetNetworkLog([FromQuery] MapFilter filters)
        {
            // The session_id check can be removed if you want to allow fetching
            // logs for multiple sessions based on other criteria in the future.
            // For now, it's good validation.
            if (filters.session_id <= 0)
            {
                return Json(new List<object>());
            }

            try
            {
                IQueryable<tbl_network_log> query = db.tbl_network_log
                                                      .Where(log => log.session_id == filters.session_id);

                // Conditionally apply filters
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

                // ✅ FIX: Order the data BEFORE applying pagination (Skip/Take)
                var paginatedQuery = query
                    .OrderBy(log => log.timestamp) // Order first
                    .Skip((filters.page - 1) * filters.limit) // Then skip
                    .Take(filters.limit); // Then take

                // ✅ FIX: The second OrderBy has been REMOVED from here.
                var logs = await paginatedQuery
                    .Select(log => new
                    {
                        log.session_id, // It's helpful to return the session_id
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
                        log.m_alpha_long, // include provider in response
                        log.mos,
                        log.volte_call
                    })
                    .ToListAsync();

                return Json(logs);
            }
            catch (Exception ex)
            {
                // For debugging, it's useful to see the error on the server
                Console.WriteLine($"Error in GetNetworkLog: {ex.Message}");
                // Return a proper 500 status code with a message
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
                // Token validation can be re-enabled if needed
                // message = cf.MatchToken(token);
                // if (message.Status == 1)

                IQueryable<tbl_prediction_data> query = db.tbl_prediction_data;

                message.Status = 1;
                string sessionIds = "";

                if (projectId.HasValue && projectId != 0)
                {
                    query = query.Where(e => e.tbl_project_id == projectId);
                }

                if (!string.IsNullOrEmpty(Band))
                    query = query.Where(e => e.band == Band);

                if (!string.IsNullOrEmpty(EARFCN))
                    query = query.Where(e => e.earfcn == EARFCN);

                if (!string.IsNullOrEmpty(State))
                {
                    //if (State == "Data") query = query.Where(e => e.apps != null && e.apps != "" && e.call_state == "Idle");
                    //if (State == "Idle") query = query.Where(e => (e.apps == null || e.apps == "") && e.call_state == "Idle");
                    //if (State == "DataVoice") query = query.Where(e => e.apps != null && e.apps != "" && e.call_state == "Off the hook");
                    //if (State == "Voice") query = query.Where(e => (e.apps == null || e.apps == "") && e.call_state == "Off the hook");
                }

                var data = query.Select(a => new
                {
                    a.lat,
                    a.lon,
                    prm = metric == "RSRP" ? a.rsrp : (metric == "RSRQ" ? a.rsrq : a.sinr)
                }).ToList();

                // Calculate averages
                double? averageRsrp = query.Where(x => x.rsrp != null).Average(x => (double?)x.rsrp);
                double? averageRsrq = query.Where(x => x.rsrq != null).Average(x => (double?)x.rsrq);
                double? averageSinr = query.Where(x => x.sinr != null).Average(x => (double?)x.sinr);

                GraphStruct CoveragePerfGraph = new GraphStruct();
                var setting = db.thresholds.FirstOrDefault(x => x.user_id == cf.UserId);
                if (setting == null)
                {
                    setting = db.thresholds.FirstOrDefault(x => x.is_default == 1);
                }
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
                //pointsinsidebuilding = 1;
                // --- Add spatial filtering condition here ---
                if (pointsInsideBuilding == 1)
                {
                    try
                    {
                        // The raw SQL query with a parameterized project_id
                        // IMPORTANT: Make sure column names here EXACTLY match properties in PredictionPointDto.
                        // If they don't, use SQL aliases (e.g., tpd.tbl_project_id AS MyProjectId).
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
                            tpd.tbl_project_id = {0} AND -- Parameter for project ID
                        ST_Contains(mr.region, ST_PointFromText(CONCAT('POINT(', tpd.lon, ' ', tpd.lat, ')'), 4326));";

                        // Execute the raw SQL query and map results to PredictionPointDto
                        var matchingPoints = db.Set<PredictionPointDto>() // Use Set<T>() for types not directly in DbSets
                                                           .FromSqlRaw(sqlQuery, projectId) // Pass the projectId as a parameter
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
                        // Log the error (e.g., using ILogger from Microsoft.Extensions.Logging)
                        Console.WriteLine($"Error fetching prediction data with raw SQL: {ex.Message}");
                        // In a production environment, avoid exposing raw error details to the client
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


        [HttpGet] // Changed endpoint name to avoid conflict if you keep the other
        public JsonResult GetPredictionDataForSelectedBuildingPolygonsRaw(int projectId, string metric)
        {
            try
            {
                // The raw SQL query with a parameterized project_id
                // IMPORTANT: Make sure column names here EXACTLY match properties in PredictionPointDto.
                // If they don't, use SQL aliases (e.g., tpd.tbl_project_id AS MyProjectId).
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
                    tpd.tbl_project_id = {0} AND -- Parameter for project ID
                    ST_Contains(mr.region, ST_PointFromText(CONCAT('POINT(', tpd.lon, ' ', tpd.lat, ')'), 4326));";

                // Execute the raw SQL query and map results to PredictionPointDto
                var matchingPoints = db.Set<PredictionPointDto>() // Use Set<T>() for types not directly in DbSets
                                                   .FromSqlRaw(sqlQuery, projectId) // Pass the projectId as a parameter
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
                // Log the error (e.g., using ILogger from Microsoft.Extensions.Logging)
                Console.WriteLine($"Error fetching prediction data with raw SQL: {ex.Message}");
                // In a production environment, avoid exposing raw error details to the client
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
                //message = cf.MatchToken(token);
                //if (message.Status == 1)
                {

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
            }
            catch (Exception ex)
            {
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }

        [HttpPost]
        [AllowAnonymous]
        //[Route("upload_image")]
        public async Task<IActionResult> UploadImage([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            // Optional: restrict to images only
            var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif" };
            if (!allowedTypes.Contains(file.ContentType))
            {
                return BadRequest("Only image files are allowed.");
            }

            // Get path to wwwroot
            //var webRootPath = _env.WebRootPath; // Inject IWebHostEnvironment _env via constructor
            var webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");

            var uploadFolder = Path.Combine(webRootPath, "uploaded_images");

            // Define upload path
            //var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "uploaded_images");

            // Create directory if not exists
            if (!Directory.Exists(uploadFolder))
                Directory.CreateDirectory(uploadFolder);

            // Use original filename
            var filePath = Path.Combine(uploadFolder, Path.GetFileName(file.FileName));

            // Save file
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
            // New optional provider filter (matches m_alpha_long in tbl_network_log)
            public string? Provider { get; set; }
            // You can add more filters here later (e.g., NetworkType, Provider)

           public int? PolygonId { get; set; }
        }

        [HttpGet]
        [Route("GetLogsByDateRange")] // A new, dedicated route
        public async Task<JsonResult> GetLogsByDateRange([FromQuery] LogFilterModel filters)
        {
            try
            {
                // Start with the base query for all network logs
                IQueryable<tbl_network_log> query = db.tbl_network_log;

                // Apply StartDate filter if provided
                if (filters.StartDate.HasValue)
                {
                    query = query.Where(log => log.timestamp >= filters.StartDate.Value);
                }

                // Apply EndDate filter if provided
                if (filters.EndDate.HasValue)
                {
                    // Add 1 day to the end date to include all logs on that day
                    var endDate = filters.EndDate.Value.AddDays(1);
                    query = query.Where(log => log.timestamp < endDate);
                }

                // Apply Provider filter if provided (matches m_alpha_long column)
                if (!string.IsNullOrEmpty(filters.Provider))
                {
                    query = query.Where(log => log.m_alpha_long == filters.Provider);
                }

                 if (filters.PolygonId.HasValue) // NEW
        query = query.Where(log => log.polygon_id == filters.PolygonId.Value);

                // IMPORTANT: To prevent crashing the server and browser,
                // limit the number of points returned in a single request.
                // 20,000 is a reasonable limit. You can adjust this.
                var logs = await query
                    .OrderBy(log => log.timestamp) // Ordering is good practice
                    .Take(20000) // Apply the limit
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
                        log.mos, // include provider in response
                        log.polygon_id 
                    })
                    .ToListAsync();

                if (!logs.Any())
                {
                    return Json(new List<object>()); // Return empty array if no results
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

        // SignalTracker/Controllers/MapViewController.cs

        [HttpGet]
        [Route("GetProviders")]
        public JsonResult GetProviders()
        {
            var providerNames = db.tbl_network_log
                // Add this 'Where' clause to filter out bad data at the database level
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
                // Also add a 'Where' clause here for robustness
                .Where(t => !string.IsNullOrEmpty(t.network))
                .Select(t => t.network)
                .Distinct()
                .ToList();

            var technologies = technologyNames
                .Select((name, index) => new { id = name, name }) // Using name for both id and name as in original code
                .ToList();

            return Json(technologies);
        }

        [HttpGet]
        [Route("GetBands")]
        public JsonResult GetBands()
        {
            try
            {
                // Get distinct band values from network logs, excluding null/empty values
                var bandNames = db.tbl_network_log
                    .Where(b => !string.IsNullOrEmpty(b.band))
                    .Select(b => b.band)
                    .Distinct()
                    .ToList();

                // Format the bands for the frontend
                var bands = bandNames
                    .Select((name, index) => new { id = index + 1, name })
                    .ToList();

                return Json(bands);
            }
            catch (Exception ex)
            {
                // Log the error for debugging
                Console.WriteLine($"Error in GetBands: {ex.Message}");

                // Return a proper error response
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
            /*using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            Console.WriteLine("Received JSON: " + body);*/
            // Now try manually deserializing:
            //var model = JsonSerializer.Deserialize<NetworkLogPostModel>(body); 

            ReturnMessage message = new ReturnMessage();

            try
            {
                //cf.SessionCheck(); // Optional: session/token validation

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
                            // Construct raw point as WKT (Well-Known Text)
                            string pointWKT = $"POINT({lon} {lat})"; // Note: longitude comes first in WKT

                            // Query polygon_id that contains the point
                            /*var polygonId = db.Database.SqlQuery<int?>($@"
                                SELECT id FROM map_region 
                                WHERE ST_Contains(region, ST_GeomFromText('{pointWKT}'))
                                LIMIT 1
                            ").FirstOrDefault(); */

                            //string pointWKT = $"POINT({item.lon} {item.lat})";

                            //SHOW INDEX FROM map_regions;
                            //ALTER TABLE defaultdb.map_regions DROP INDEX region;
                            //ALTER TABLE defaultdb.map_regions MODIFY region geometry NOT NULL SRID 4326;
                            //ALTER TABLE defaultdb.map_regions ADD SPATIAL INDEX(region);

                            int srid = 4326; // Most GIS systems use WGS 84

                            var polygonId = db.PolygonMatches
                             .FromSqlRaw(@"
                               SELECT id FROM map_regions 
                               WHERE ST_Contains(region, ST_GeomFromText({0}, {1})) 
                               LIMIT 1", pointWKT, srid)
                             .Select(p => (int?)p.id)
                             .FirstOrDefault();
                            /*
                         var polygonId = db.PolygonMatches
                              .FromSqlRaw(
                                  "SELECT id FROM map_regions WHERE ST_Contains(region, ST_SRID(ST_GeomFromText(@point), @srid)) LIMIT 1",
                                  new MySql.Data.MySqlClient.MySqlParameter("@point", pointWKT),
                                  new MySql.Data.MySqlClient.MySqlParameter("@srid", srid)
                              )
                              .Select(p => (int?)p.id)
                              .FirstOrDefault();
                          */
                            /*
                            var polygonId = db.PolygonMatches
                                .FromSqlRaw(
                                    @"SELECT id FROM map_regions 
                                      WHERE ST_Contains(region, ST_SRID(ST_GeomFromText(@point), @srid)) 
                                      LIMIT 1",
                                    new MySqlParameter("@point", pointWKT),
                                    new MySqlParameter("@srid", srid)
                                )
                                .Select(p => (int?)p.id)
                                .FirstOrDefault();
                            */
                            log.polygon_id = polygonId;
                        }
                        catch (Exception ex)
                        {

                            var a = "Error: " + ex.Message;
                        }
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
