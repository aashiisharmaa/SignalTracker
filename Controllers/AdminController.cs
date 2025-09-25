using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SignalTracker.Helper;
using SignalTracker.Models;
using System;
using System.Drawing;
using System.Globalization;
using System.Reflection.Emit;
using System.Web;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
//using System.Data.Entity.SqlServer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

namespace SignalTracker.Controllers
{
    [Route("Admin/[action]")]
    public class AdminController : BaseController
    {
        ApplicationDbContext db = null;
        CommonFunction cf = null;
        public AdminController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            db = context;
            cf = new CommonFunction(context, httpContextAccessor);
        }
        public IActionResult Index()
        {
            if (!cf.SessionCheck())
                return RedirectToAction("Index", "Home");
            return View();
        }
        public IActionResult Dashboard()
        {
            if (!IsAngularRequest() || !cf.SessionCheck())
            {
                return RedirectToAction("Index", "Home");
            }
            ViewBag.UserType = cf.UserType;
            return View();
        }
        [HttpGet]
        [HttpGet]
        public JsonResult GetReactDashboardData()
        {
            ReturnAPIResponse message = new ReturnAPIResponse();
            try
            {
                // SessionCheck is important for security
                // For development, you might temporarily comment it out if it causes issues,
                // but ensure it's active in production.
                // cf.SessionCheck(); 

                message.Status = 1;

                // --- 1. Basic Stats ---
                int totalSessions = db.tbl_session.Count();
                int totalOnlineSessions = db.tbl_session.Count(s => s.end_time == null);
                int totalSamples = db.tbl_network_log.Count();
                int totalUsers = db.tbl_session.Select(s => s.user_id).Distinct().Count();

                // --- 2. Chart Data Calculations ---

                // Chart 1: Monthly Samples
                var monthlySampleCounts = db.tbl_network_log
                    .Where(n => n.timestamp.HasValue)
                    .GroupBy(n => new { n.timestamp.Value.Year, n.timestamp.Value.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .Select(g => new { month = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("yyyy-MM"), count = g.Count() })
                    .ToList();

                // Chart 2: Operator wise Samples
                var operatorWiseSamples = db.tbl_network_log
                    .Where(a => !string.IsNullOrEmpty(a.m_alpha_long))
                    .GroupBy(n => n.m_alpha_long)
                    .Select(g => new { name = g.Key, value = g.Count() })
                    .ToList();

                // Chart 3: Network Type Distribution
                var networkTypeDistribution = db.tbl_network_log
                    .Where(n => !string.IsNullOrEmpty(n.network))
                    .GroupBy(n => n.network)
                    .Select(g => new { name = g.Key, value = g.Count() })
                    .ToList();

                // Chart 4: Average RSRP Per Operator
                var avgRsrpPerOperator = db.tbl_network_log
                    .Where(n => !string.IsNullOrEmpty(n.m_alpha_long) && n.rsrp.HasValue)
                    .GroupBy(n => n.m_alpha_long)
                    .Select(g => new { name = g.Key, value = Math.Round(g.Average(item => item.rsrp.Value), 2) })
                    .ToList();

                // Chart 5: Band Distribution
                var bandDistribution = db.tbl_network_log
                    .Where(n => !string.IsNullOrEmpty(n.band))
                    .GroupBy(n => n.band)
                    .Select(g => new { name = "Band " + g.Key, value = g.Count() })
                    .ToList();

                // Chart 6: Handset wise Distribution
                var handsetDistribution = (from user in db.tbl_user
                                           join session in db.tbl_session on user.id equals session.user_id
                                           where !string.IsNullOrEmpty(user.make)
                                           group user by user.make into g
                                           select new { name = g.Key, value = g.Count() }).ToList();


                // --- 3. Assemble the Response ---
                message.Data = new
                {
                    // Top-level stats
                    totalSessions,
                    totalOnlineSessions,
                    totalSamples,
                    totalUsers,
                    // Data for each chart
                    monthlySampleCounts,
                    operatorWiseSamples,
                    networkTypeDistribution,
                    avgRsrpPerOperator,
                    bandDistribution,
                    handsetDistribution
                };
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = "An error occurred while fetching dashboard data: " + ex.Message;
            }

            return Json(message);
        }
        [HttpGet]
        public JsonResult GetDashboardData_old()
        {
            ReturnAPIResponse message = new ReturnAPIResponse();

            try
            {
                cf.SessionCheck();
                message.Status = 1;

                var today = DateTime.Today;

                // Total number of sessions
                int totalSessions = db.tbl_session.Count();

                // Total online sessions (today's sessions with no end_time)
                int totalOnlineSessions = db.tbl_session
                    .Where(s => s.start_time != null && s.end_time == null && s.start_time.Value.Date == today)
                    .Count();

                // Total number of samples
                int totalSamples = db.tbl_network_log.Count();

                // Number of users
                int totalUsers = db.tbl_session.Select(s => s.user_id).Distinct().Count();

                // Number of network types
                int totalNetworkTypes = db.tbl_network_log
                    .Where(x => x.network != null && x.network != "")
                    .Select(x => x.network)
                    .Distinct()
                    .Count();

                // Network Type Distribution (pie chart)
                var networkTypeDistribution_horizontal_bar = db.tbl_network_log
                    .Where(x => x.network != null && x.network != "")
                    .GroupBy(x => x.network)
                    .Select(g => new
                    {
                        network = g.Key,
                        count = g.Count()
                    }).ToList();

                // Samples grouped by m_alpha_long (for pie chart)
                var samplesByAlphaLong = db.tbl_network_log
                    .Where(a => a.m_alpha_long != null && a.m_alpha_long != "")
                    .GroupBy(n => n.m_alpha_long)
                    .Select(g => new
                    {
                        m_alpha_long = g.Key,
                        count = g.Count()
                    }).ToList();

                // Samples added per month for the last 6 months (bar chart)
                DateTime sixMonthsAgo = today.AddMonths(-5); // includes current month
                var monthlySampleCounts = db.tbl_network_log
                    .Where(n => n.timestamp >= sixMonthsAgo)
                    .GroupBy(n => new { n.timestamp.Value.Year, n.timestamp.Value.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .Select(g => new
                    {
                        month = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("yyyy-MM"),
                        count = g.Count()
                    }).ToList();

                var networkLogs = db.tbl_network_log
                    .Where(x => x.rsrp != null && x.sinr != null && x.m_alpha_long != null)
                    .ToList();

                var avgRsrpSinrPerOperator_bar = networkLogs
                    .Where(x => x.rsrp != null && x.sinr != null)
                    .GroupBy(x => x.m_alpha_long)
                    .Select(g => new
                    {
                        Operator = g.Key,
                        AvgRSRP = Math.Round(g.Average(x => x.rsrp.Value), 2),
                        AvgSINR = Math.Round(g.Average(x => x.sinr.Value), 2)
                    })
                    .ToList();



                // Most used band for pie chart
                var bandDistribution_pie = db.tbl_network_log
                    .Where(x => !string.IsNullOrEmpty(x.band))
                    .GroupBy(x => x.band)
                    .Select(g => new
                    {
                        band = g.Key,
                        count = g.Count()
                    }).OrderByDescending(x => x.count)
                    .ToList();

                message.Data = new
                {
                    totalSessions,
                    totalOnlineSessions,
                    totalSamples,
                    totalUsers,
                    totalNetworkTypes,
                    networkTypeDistribution_horizontal_bar, // pie
                    samplesByAlphaLong,      // pie
                    monthlySampleCounts,     // bar
                    avgRsrpSinrPerOperator_bar,  // bar
                    bandDistribution_pie             // pie
                };
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }

            return Json(message);
        }
        [HttpGet]
        public JsonResult GetDashboardGraphData()
        {
            ReturnAPIResponse message = new ReturnAPIResponse();

            try
            {
                cf.SessionCheck();
                message.Status = 1;

                var today = DateTime.Today;


                // Network Type Distribution (pie chart)
                var networkTypeDistribution_horizontal_bar = db.tbl_network_log
                    .Where(x => x.network != null && x.network != "")
                    .GroupBy(x => x.network)
                    .Select(g => new
                    {
                        network = g.Key,
                        count = g.Count()
                    }).ToList();


                var networkLogs = db.tbl_network_log
                    .Where(x => x.rsrp != null && x.sinr != null && x.m_alpha_long != null);

                /*var avgRsrpSinrPerOperator_bar = networkLogs
                    .Where(x => x.rsrp != null && x.sinr != null)
                    .GroupBy(x => x.m_alpha_long)
                    .Select(g => new
                    {
                        Operator = g.Key,
                        AvgRSRP = Math.Round(g.Average(x => x.rsrp.Value), 2),
                        AvgSINR = Math.Round(g.Average(x => x.sinr.Value), 2)
                    })
                    .ToList();
                */

                var avgRsrpSinrPerOperator_bar = networkLogs
                    .Where(x => x.rsrp != null && x.sinr != null)
                    .GroupBy(x => x.m_alpha_long)
                    .Select(g => new
                    {
                        Operator = g.Key,
                        AvgRSRP = Math.Round(g.Average(x => x.rsrp.Value), 2)

                    }).OrderByDescending(x => x.AvgRSRP)
                    .ToList();

                // Most used band for pie chart
                var bandDistribution_pie = db.tbl_network_log
                    .Where(x => !string.IsNullOrEmpty(x.band))
                    .GroupBy(x => x.band)
                    .Select(g => new
                    {
                        band = g.Key,
                        count = g.Count()
                    }).OrderByDescending(x => x.count)
                    .ToList();

                /*IQueryable<tbl_network_log> query = db.tbl_network_log;
                var metric = "RSRP";
                var groupedLogs = (from user in db.tbl_user
                                   join session in db.tbl_session on user.id equals session.user_id into userSessions
                                   from session in userSessions.DefaultIfEmpty()
                                   join log in query on session.id equals log.session_id into sessionLogs
                                   from log in sessionLogs.DefaultIfEmpty()
                                   where !string.IsNullOrEmpty(user.make)
                                   select new
                                   {
                                       make = user.make.ToLower(),
                                       log
                                   }).ToList();  // 🚨 Query ends here; switch to in-memory processing

                var handsetWiseAvg = groupedLogs
                        .GroupBy(x => x.make)
                        .Select(g => new
                        {
                            Make = g.Key,
                            Avg = Math.Round(
                                (decimal)(
                                    metric == "RSRP" ? (g.Where(x => x.log != null && x.log.rsrp.HasValue).Any() ? g.Where(x => x.log != null && x.log.rsrp.HasValue).Average(x => x.log.rsrp.Value) : 0.0) :
                                    
                                    0.0 // Default value for when metric doesn't match
                                ),
                                2
                            )
                        }).ToList();
                */

                IQueryable<tbl_network_log> query = db.tbl_network_log;
                var metric = "RSRP";

                var handsetWiseAvg_bar = (from user in db.tbl_user
                                          join session in db.tbl_session on user.id equals session.user_id into userSessions
                                          from session in userSessions.DefaultIfEmpty()
                                          join log in query on session.id equals log.session_id into sessionLogs
                                          from log in sessionLogs.DefaultIfEmpty()
                                          where !string.IsNullOrEmpty(user.make)
                                          // FIX: Group by a composite key containing both original and lowercase make
                                          group new { user, log } by new { MakeOriginal = user.make, MakeLower = user.make.ToLower() } into g
                                          select new
                                          {
                                              // Select the original make value from the group's key
                                              Make = g.Key.MakeOriginal,
                                              Avg = (decimal)Math.Round(
                                                  g.Any(x => x.log != null && x.log.rsrp.HasValue) ?
                                                  g.Where(x => x.log != null && x.log.rsrp.HasValue)
                                                   .Average(x => x.log.rsrp.Value) :
                                                  0.0, 2)
                                          }).ToList();



                message.Data = new
                {
                    networkTypeDistribution_horizontal_bar, // pie
                    avgRsrpSinrPerOperator_bar,  // bar
                    bandDistribution_pie,             // pie
                    handsetWiseAvg_bar
                };
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }

            return Json(message);
        }

        [HttpPost]
        public JsonResult GetAllUsers()
        {
            int? UserType = 1;
            if (HttpContext?.Session.GetString("UserType") != null)
            {
                UserType = HttpContext?.Session.GetInt32("UserType");
            }
            if (UserType == 1)
            {
                var FinancialYear = db.tbl_user.Where(a => a.isactive == 1).OrderBy(a => a.name).ToList();
                return Json(FinancialYear);
            }
            else
            {
                var FinancialYear = db.tbl_user.Where(a => a.isactive == 1).OrderBy(a => a.name).ToList();
                return Json(FinancialYear);
            }
        }
        [HttpGet]

        #region ManageUsers
        public ActionResult ManageUsers()
        {
            if (!IsAngularRequest())
            {
                return RedirectToAction("Index", "Home");
            }
            if (!cf.SessionCheck("1"))
            {
                return RedirectToAction("Dashboard", "Admin");
            }
            return View();
        }
        [HttpGet]
        public JsonResult GetUsers(string token, string UserName, string Email, string Mobile)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message = cf.MatchToken(token);
                message.Status = 1;
                if (message.Status == 1)
                {
                    var GetUser = (from ob_user in db.tbl_user//.Where(a => a.isactive == 1)// && (a.m_user_type_id == 2 || a.m_user_type_id == 3 || a.m_user_type_id == 4))
                                                              //join ob_state in db.tbl_state on ob_user.tbl_state_id equals ob_state.id
                                   select new
                                   {
                                       ob_user = ob_user,
                                       //ob_state = ob_state
                                   }
                                 ).ToList();

                    //if (HttpContext?.Session.GetInt32("UserType") != 1)
                    //{
                    //    int? UserId = HttpContext?.Session.GetInt32("UserType");
                    //    GetUser = GetUser.Where(a => a.ob_user.id == UserId).ToList();
                    //}


                    if (GetUser != null)
                    {

                        if (UserName != null && UserName != "")
                            GetUser = GetUser.Where(a => a.ob_user.name.ToLower().Contains(UserName.ToLower())).ToList();

                        if (Email != null && Email != "")
                            GetUser = GetUser.Where(a => a.ob_user.email.ToLower().Contains(Email.ToLower())).ToList();

                        if (Mobile != null && Mobile != "")
                            GetUser = GetUser.Where(a => a.ob_user.mobile.ToLower().Contains(Mobile.ToLower())).ToList();
                    }
                    foreach (var users in GetUser)
                    {
                        if (users.ob_user.password != null && users.ob_user.password != "")
                        {
                            string star = "";
                            for (int i = 0; i < 15; i++)
                            {
                                star += "*";
                            }
                            users.ob_user.password = star;
                        }
                    }
                    message.Data = GetUser;
                }
            }
            catch (Exception ex)
            {
                Writelog writelog = new Writelog(db);
                writelog.write_exception_log(0, "AdminHomeController", "GetUsers", DateTime.Now, ex);
            }
            return Json(message);
        }
        #endregion
        #region Manage User
        public ActionResult ManageUser()
        {
            return View();
        }
        [HttpGet]
        public JsonResult GetUserById(string token, int UserID)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message.Status = 1;// = cf.MatchToken(token);
                if (message.Status == 1)
                {
                    var GetUser = db.tbl_user.Where(a => a.isactive == 1 && a.id == UserID).FirstOrDefault();

                    if (GetUser.password != null && GetUser.password != "")
                    {
                        string star = "";
                        for (int i = 0; i < 15; i++)
                        {
                            star += "*";
                        }
                        GetUser.password = star;
                    }
                    message.Data = GetUser;
                }
            }
            catch (Exception ex)
            {
                Writelog writelog = new Writelog(db);
                writelog.write_exception_log(0, "AdminHomeController", "GetUserById", DateTime.Now, ex);
            }
            return Json(message);
        }
        public static string DecodeFrom64(string encodedData)
        {
            System.Text.UTF8Encoding encoder = new System.Text.UTF8Encoding();
            System.Text.Decoder utf8Decode = encoder.GetDecoder();
            byte[] todecode_byte = Convert.FromBase64String(encodedData);
            int charCount = utf8Decode.GetCharCount(todecode_byte, 0, todecode_byte.Length);
            char[] decoded_char = new char[charCount];
            utf8Decode.GetChars(todecode_byte, 0, todecode_byte.Length, decoded_char, 0);
            string result = new String(decoded_char);
            return result;


        }
        public static string EncodePasswordToBase64(string password)
        {
            try
            {
                byte[] encData_byte = new byte[password.Length];
                encData_byte = System.Text.Encoding.UTF8.GetBytes(password);
                string encodedData = Convert.ToBase64String(encData_byte);
                return encodedData;
            }
            catch (Exception ex)
            {
                throw new Exception("Error in base64Encode" + ex.Message);
            }
        }
        [HttpPost]
        public JsonResult SaveUserDetails([FromForm] IFormCollection values, tbl_user users, string token1, string ip)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message.Status = 1;// = cf.MatchToken(token1);
                if (message.Status == 1)
                {
                    users.name = HttpUtility.HtmlEncode(users.name);
                    users.email = HttpUtility.HtmlEncode(users.email);
                    users.mobile = HttpUtility.HtmlEncode(users.mobile);

                    if (users.id == 0)
                    {
                        var GetUser = db.tbl_user.Where(a => a.email == users.email && a.isactive == 1).FirstOrDefault();
                        if (GetUser == null)
                        {
                            users.date_created = DateTime.Now;
                            users.isactive = 1;
                            db.tbl_user.Add(users);
                            db.SaveChanges();
                            message.Status = 1;
                            message.Message = DisplayMessage.UserDetailsSaved;
                        }
                        else
                            message.Message = DisplayMessage.UserExist;
                    }
                    else
                    {
                        var GetUser = db.tbl_user.Where(a => a.id == users.id).FirstOrDefault();
                        if (GetUser != null)
                        {
                            GetUser.name = users.name;
                            GetUser.email = users.email;
                            GetUser.mobile = users.mobile;
                            GetUser.m_user_type_id = users.m_user_type_id;
                            // GetUser.password = users.password;
                            db.Entry(GetUser).State = EntityState.Modified;
                            db.SaveChanges();
                            message.Status = 2;
                            message.Message = DisplayMessage.UserDetailsUpdated;
                        }
                    }
                    message.token = "";// cf.CreateToken(ip);
                }

            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }
        [HttpPost]
        public JsonResult GetUser(int UserID, string token)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message = cf.MatchToken(token);
                if (message.Status == 1)
                {
                    var GetUser = db.tbl_user.Where(a => a.id == UserID).FirstOrDefault();
                    if (GetUser.password != null && GetUser.password != "")
                    {
                        GetUser.password = "";
                    }
                    message.Data = GetUser;
                }
            }
            catch (Exception ex)
            {
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }
        [HttpPost]
        public JsonResult DeleteUser(int id, string ip)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message.Status = 1;//message = cf.MatchToken(token);
                if (message.Status == 1)
                {
                    var GetUser = db.tbl_user.Where(a => a.id == id).FirstOrDefault();
                    if (GetUser != null)
                    {
                        GetUser.isactive = 2;
                        db.Entry(GetUser).State = EntityState.Modified;
                        db.SaveChanges();
                        message.Status = 1;
                        message.Message = DisplayMessage.UserDeleted;
                        if (message.Status == 1)
                            message.token = cf.CreateToken(ip);
                    }
                }
            }
            catch (Exception ex)
            {
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }
        [HttpPost]
        public JsonResult UserResetPassword(int userid, string newpwd, string captcha)
        {
            ReturnMessage ret = new ReturnMessage();
            try
            {
                //if (HttpContext?.Session.GetString("CaptchaImageText") == captcha)
                {
                    var GetUser = db.tbl_user.Where(a => a.id == userid).FirstOrDefault();
                    if (GetUser != null)
                    {
                        GetUser.password = newpwd;
                        db.Entry(GetUser).State = EntityState.Modified;
                        db.SaveChanges();
                        ret.Status = 1;
                        ret.Message = "Password has been reset successfully.";
                    }
                    else
                    {
                        ret.Status = 0;
                        ret.Message = "Invalid Request";
                    }
                }
                //else
                //{
                //    ret.Status = 0;
                //    ret.Message = "Invalid CAPTCHA Code !";
                //}
            }
            catch (Exception ex)
            {
                ret.Status = 0;
                ret.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(ret);
        }
        [HttpPost]
        public JsonResult ChangePassword(int userid, string oldpwd, string newpwd, string captcha)
        {
            ReturnMessage ret = new ReturnMessage();
            try
            {
                if (HttpContext?.Session.GetString("CaptchaImageText") == captcha)
                {
                    var GetUser = db.tbl_user.Where(a => a.id == userid && a.password == oldpwd).FirstOrDefault();
                    if (GetUser != null)
                    {
                        GetUser.password = newpwd;
                        db.Entry(GetUser).State = EntityState.Modified;
                        db.SaveChanges();
                        ret.Status = 1;
                    }
                    else
                    {
                        ret.Status = 0;
                        ret.Message = "Old password is wrong";
                    }
                }
                else
                {
                    ret.Status = 0;
                    ret.Message = "Invalid CAPTCHA Code !";
                }
            }
            catch (Exception ex)
            {
                ret.Status = 0;
                ret.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(ret);
        }
        #endregion
        #region Manage Sessions
        public ActionResult ManageSession()
        {
            if (!IsAngularRequest() || !cf.SessionCheck())
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetAllNetworkLogs()
        {
            try
            {
                // A single, efficient query to get all network logs that have a location.
                var allLogs = await db.tbl_network_log
                    .Where(log => log.lat != null && log.lon != null)
                    .Select(log => new
                    {
                        log.session_id,
                        log.lat,
                        log.lon,
                        log.rsrp,
                        log.rsrq,
                        log.sinr,
                        log.network,
                        log.timestamp
                    })
                    .ToListAsync();

                return Json(allLogs);
            }
            catch (Exception ex)
            {
                // Return a 500 error with a message for debugging.
                Response.StatusCode = 500;
                return Json(new { Message = "An error occurred on the server: " + ex.Message });
            }
        }


        [HttpGet]
        public async Task<JsonResult> GetSessions()
        {
            try
            {
                // This query now joins the session and user tables.
                var sessions = await (
                    from s in db.tbl_session
                    join u in db.tbl_user on s.user_id equals u.id
                    orderby s.start_time descending
                    select new
                    {
                        // Core Session Info
                        id = s.id,
                        session_name = "Session " + s.id,
                        start_time = s.start_time,
                        end_time = s.end_time,
                        notes = s.notes,

                        // CRITICAL: Including the start location
                        start_lat = s.start_lat,
                        start_lon = s.start_lon,
                        end_lat = (double?)s.end_lat,
                        end_lon = (double?)s.end_lon,
                        capture_frequency = (double?)s.capture_frequency,

                        // User details joined from the user table
                        CreatedBy = u.name,
                        mobile = u.mobile,
                        make = u.make,
                        model = u.model,
                        os = u.os,
                        operator_name = u.operator_name,
                        distance_km = s.distance,
                        start_address = s.start_address,
                        end_address = s.end_address

                    })
                    .ToListAsync();

                // The frontend expects the array of sessions directly.
                return Json(sessions);
            }
            catch (Exception ex)
            {
                // This provides a standard error response that the frontend can handle.
                Response.StatusCode = 500;
                return Json(new { Message = "An error occurred on the server: " + ex.Message });
            }
        }


       [HttpGet]
public async Task<JsonResult> GetSessionsByDateRange(string startDateIso, string endDateIso)
{
    try
    {
        if (!DateTime.TryParse(startDateIso, out DateTime startDate) ||
            !DateTime.TryParse(endDateIso, out DateTime endDate))
        {
            return Json(new { success = false, Message = "Invalid date format" });
        }

        endDate = endDate.Date.AddDays(1).AddTicks(-1);

        // --- Step 1: Fetch the main session and user data that matches the date range ---
        var sessionsData = await (
            from s in db.tbl_session
            join u in db.tbl_user on s.user_id equals u.id
            where s.start_time.HasValue && s.start_time.Value >= startDate && s.start_time.Value <= endDate
            select new
            {
                // Session info
                id = s.id,
                session_name = "Session " + s.id,
                start_time = s.start_time,
                end_time = s.end_time,
                notes = s.notes,
                start_lat = (double?)s.start_lat,
                start_lon = s.start_lon,
                end_lat = s.end_lat,
                end_lon = s.end_lon,
                capture_frequency = s.capture_frequency,
                distance_km = s.distance,
                start_address = s.start_address,
                end_address = s.end_address,

                // User info
                CreatedBy = u.name,
                mobile = u.mobile,
                make = u.make,
                model = u.model,
                os = u.os,
                operator_name = u.operator_name
            })
            .ToListAsync(); // Execute the first query and bring sessions into memory

        // --- Step 2: Efficiently fetch all related logs in a single, separate query ---
        var sessionIds = sessionsData.Select(s => s.id).ToList();
        
        var allLogsForSessions = await db.tbl_network_log
            .Where(log => sessionIds.Contains(log.session_id))
            .ToListAsync();

        // Group the logs by session_id in memory for fast lookups
        var logsLookup = allLogsForSessions.ToLookup(log => log.session_id);

        // --- Step 3: Combine the sessions and their logs in your application code ---
        var finalResult = sessionsData.Select(s => new
        {
            // Copy all the session and user properties
            s.id,
            s.session_name,
            s.start_time,
            s.end_time,
            s.notes,
            s.start_lat,
            s.start_lon,
            s.end_lat,
            s.end_lon,
            s.capture_frequency,
            s.distance_km,
            s.start_address,
            s.end_address,
            s.CreatedBy,
            s.mobile,
            s.make,
            s.model,
            s.os,
            s.operator_name,

            // Assign the looked-up logs to each session
            Logs = logsLookup[s.id].Select(l => new
            {
                l.lat,
                l.lon,
                l.rsrp,
                l.rsrq,
                l.sinr,
                l.ul_tpt,
                l.dl_tpt,
                l.band,
                l.network,
                l.m_alpha_long,
                l.timestamp
            }).ToList()
        }).ToList();

        return Json(finalResult);
    }
    catch (Exception ex)
    {
        Response.StatusCode = 500;
        return Json(new { Message = "Error fetching sessions: " + ex.Message });
    }
}

        [HttpDelete("DeleteSession")]
        public async Task<IActionResult> DeleteSession([FromQuery] string id)
        {
            try
            {
                Console.WriteLine("Hello, World!");
                if (!int.TryParse(id, out int sessionId))
                    return BadRequest("Invalid session id");
                // int sed = Convert.ToInt32(id);
                var session = await db.tbl_session.FindAsync(sessionId);

                if (session == null)
                {
                    return NotFound(new { success = false, message = "Session not found." });
                }

                var logs = await db.tbl_network_log
                    .Where(l => l.session_id == sessionId)
                    .ToListAsync();

                if (logs.Any())
                {
                    db.tbl_network_log.RemoveRange(logs);
                }

                db.tbl_session.Remove(session);
                await db.SaveChangesAsync();

                return Ok(new { success = true, message = "Session deleted successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "An error occurred: " + ex.Message
                });
            }
        }


        #endregion




    }
}
