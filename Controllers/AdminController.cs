using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SignalTracker.Helper;
using SignalTracker.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SignalTracker.Controllers
{
    [Route("Admin/[action]")]
    public class AdminController : BaseController
    {
        // DI
        private readonly ApplicationDbContext db;
        private readonly CommonFunction cf;
        private readonly IDbContextFactory<ApplicationDbContext> dbFactory;
        private readonly IMemoryCache cache;

        public AdminController(
            ApplicationDbContext context,
            IHttpContextAccessor httpContextAccessor,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            IMemoryCache cache)
        {
            db = context;
            cf = new CommonFunction(context, httpContextAccessor);
            this.dbFactory = dbFactory;
            this.cache = cache;
        }

        // ========= Basic Nav =========
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

        // ========= DTOs =========
        // We intentionally keep these fields as object? so they can accept whatever type
        // your EF model has (string? or double?) without casting problems.
        private sealed class LogDto
        {
            public int session_id { get; set; }
            public object? lat { get; set; }
            public object? lon { get; set; }
            public object? rsrp { get; set; }
            public object? rsrq { get; set; }
            public object? sinr { get; set; }
            public object? ul_tpt { get; set; }
            public object? dl_tpt { get; set; }
            public object? band { get; set; }
            public object? network { get; set; }
            public object? m_alpha_long { get; set; }
            public object? timestamp { get; set; }
        }

        private sealed class UserListItemDto
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public string? Email { get; set; }
            public string? Mobile { get; set; }
            public int? UserTypeId { get; set; }
            public int IsActive { get; set; }
            public DateTime? DateCreated { get; set; }
            public string? Make { get; set; }
            public string? Model { get; set; }
            public string? Os { get; set; }
            public string? OperatorName { get; set; }
        }

        // ========= FAST Dashboard (parallel contexts + caching) =========
        // NOTE: Add to Program.cs:
        //   builder.Services.AddPooledDbContextFactory<ApplicationDbContext>(...);
        //   builder.Services.AddMemoryCache();
        [HttpGet]
        public async Task<JsonResult> GetReactDashboardData(bool lite = false, CancellationToken ct = default)
        {
            var message = new ReturnAPIResponse { Status = 1 };

            try
            {
                // if (!cf.SessionCheck()) { message.Status = 0; message.Message = "Unauthorized"; return Json(message); }

                // ---- Super fast lite mode (stats only) ----
                if (lite)
                {
                    const string liteKey = "dash:stats:lite";
                    if (!cache.TryGetValue(liteKey, out object? liteData))
                    {
                        var totalSessions       = await db.tbl_session.AsNoTracking().CountAsync(ct);
                        var totalOnlineSessions = await db.tbl_session.AsNoTracking().CountAsync(s => s.end_time == null, ct);
                        var totalSamples        = await db.tbl_network_log.AsNoTracking().CountAsync(ct);
                        var totalUsers          = await db.tbl_session.AsNoTracking().Select(s => s.user_id).Distinct().CountAsync(ct);

                        liteData = new
                        {
                            totalSessions,
                            totalOnlineSessions,
                            totalSamples,
                            totalUsers
                        };
                        cache.Set(liteKey, liteData, TimeSpan.FromSeconds(30));
                    }
                    message.Data = liteData!;
                    return Json(message);
                }

                // ---- Full dashboard (parallel via separate contexts) ----
                const string fullKey = "dash:full:v3";
                if (!cache.TryGetValue(fullKey, out object? fullData))
                {
                    // helper to run a function on its own DbContext safely in parallel
                    async Task<T> Run<T>(Func<ApplicationDbContext, Task<T>> work)
                    {
                        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
                        return await work(ctx);
                    }

                    var tTotalSessions       = Run(c => c.tbl_session.AsNoTracking().CountAsync(ct));
                    var tTotalOnlineSessions = Run(c => c.tbl_session.AsNoTracking().CountAsync(s => s.end_time == null, ct));
                    var tTotalSamples        = Run(c => c.tbl_network_log.AsNoTracking().CountAsync(ct));
                    var tTotalUsers          = Run(c => c.tbl_session.AsNoTracking().Select(s => s.user_id).Distinct().CountAsync(ct));

                    var tMonthly = Run(c => c.tbl_network_log.AsNoTracking()
                        .Where(n => n.timestamp != null)
                        .GroupBy(n => new { n.timestamp!.Value.Year, n.timestamp!.Value.Month })
                        .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToListAsync(ct));

                    var tOpWise = Run(c => c.tbl_network_log.AsNoTracking()
                        .Where(a => a.m_alpha_long != null && a.m_alpha_long != "")
                        .GroupBy(n => n.m_alpha_long!)
                        .Select(g => new { name = g.Key, value = g.Count() })
                        .OrderByDescending(x => x.value)
                        .ToListAsync(ct));

                    var tNetworkType = Run(c => c.tbl_network_log.AsNoTracking()
                        .Where(n => n.network != null && n.network != "")
                        .GroupBy(n => n.network!)
                        .Select(g => new { name = g.Key, value = g.Count() })
                        .OrderByDescending(x => x.value)
                        .ToListAsync(ct));

                    var tAvgRsrp = Run(c => c.tbl_network_log.AsNoTracking()
                        .Where(n => n.m_alpha_long != null && n.m_alpha_long != "" && n.rsrp != null)
                        .GroupBy(n => n.m_alpha_long!)
                        .Select(g => new { name = g.Key, value = Math.Round(g.Average(item => item.rsrp!.Value), 2) })
                        .OrderByDescending(x => x.value)
                        .ToListAsync(ct));

                    var tBandRows = Run(c => c.tbl_network_log.AsNoTracking()
                        .Where(n => n.band != null && n.band != "")
                        .GroupBy(n => n.band!)
                        .Select(g => new { Band = g.Key, Count = g.Count() })
                        .OrderByDescending(x => x.Count)
                        .ToListAsync(ct));

                    var tHandset = Run(c =>
                        (from user in c.tbl_user.AsNoTracking()
                         join session in c.tbl_session.AsNoTracking() on user.id equals session.user_id
                         where !string.IsNullOrEmpty(user.make)
                         group user by user.make! into g
                         select new { name = g.Key, value = g.Count() }).ToListAsync(ct));

                    await Task.WhenAll(
                        tTotalSessions, tTotalOnlineSessions, tTotalSamples, tTotalUsers,
                        tMonthly, tOpWise, tNetworkType, tAvgRsrp, tBandRows, tHandset
                    );

                    var monthlySampleCounts = tMonthly.Result
                        .Select(x => new { month = $"{x.Year:D4}-{x.Month:D2}", count = x.Count })
                        .ToList();

                    var bandDistribution = tBandRows.Result
                        .Select(x => new { name = "Band " + x.Band, value = x.Count })
                        .ToList();

                    fullData = new
                    {
                        totalSessions       = tTotalSessions.Result,
                        totalOnlineSessions = tTotalOnlineSessions.Result,
                        totalSamples        = tTotalSamples.Result,
                        totalUsers          = tTotalUsers.Result,
                        monthlySampleCounts,
                        operatorWiseSamples     = tOpWise.Result,
                        networkTypeDistribution = tNetworkType.Result,
                        avgRsrpPerOperator      = tAvgRsrp.Result,
                        bandDistribution,
                        handsetDistribution     = tHandset.Result
                    };

                    cache.Set(fullKey, fullData, TimeSpan.FromSeconds(30));
                }

                message.Data = fullData!;
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = "An error occurred while fetching dashboard data: " + ex.Message;
            }

            return Json(message);
        }

        // ========= Legacy “old” dashboard (sequential, safe) =========
        [HttpGet]
        public async Task<JsonResult> GetDashboardData_old(CancellationToken ct = default)
        {
            var message = new ReturnAPIResponse();

            try
            {
                cf.SessionCheck();
                message.Status = 1;

                var today = DateTime.Today;

                var totalSessions = await db.tbl_session.AsNoTracking().CountAsync(ct);
                var totalOnlineSessions = await db.tbl_session.AsNoTracking()
                    .Where(s => s.start_time != null && s.end_time == null && s.start_time!.Value.Date == today)
                    .CountAsync(ct);
                var totalSamples = await db.tbl_network_log.AsNoTracking().CountAsync(ct);
                var totalUsers = await db.tbl_session.AsNoTracking().Select(s => s.user_id).Distinct().CountAsync(ct);

                var networkTypeDistributionTask = await db.tbl_network_log.AsNoTracking()
                    .Where(x => x.network != null && x.network != "")
                    .GroupBy(x => x.network!)
                    .Select(g => new { network = g.Key, count = g.Count() })
                    .ToListAsync(ct);

                var samplesByAlphaLong = await db.tbl_network_log.AsNoTracking()
                    .Where(a => a.m_alpha_long != null && a.m_alpha_long != "")
                    .GroupBy(n => n.m_alpha_long!)
                    .Select(g => new { m_alpha_long = g.Key, count = g.Count() })
                    .ToListAsync(ct);

                DateTime sixMonthsAgo = today.AddMonths(-5);
                var monthlyRaw = await db.tbl_network_log.AsNoTracking()
                    .Where(n => n.timestamp >= sixMonthsAgo)
                    .GroupBy(n => new { n.timestamp!.Value.Year, n.timestamp!.Value.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .Select(g => new { Year = g.Key.Year, Month = g.Key.Month, Count = g.Count() })
                    .ToListAsync(ct);

                var monthlySampleCounts = monthlyRaw
                    .Select(x => new { month = new DateTime(x.Year, x.Month, 1).ToString("yyyy-MM"), count = x.Count })
                    .ToList();

                var avgRsrpSinr = await db.tbl_network_log.AsNoTracking()
                    .Where(x => x.rsrp != null && x.sinr != null && x.m_alpha_long != null)
                    .GroupBy(x => x.m_alpha_long!)
                    .Select(g => new
                    {
                        Operator = g.Key,
                        AvgRSRP = Math.Round(g.Average(x => x.rsrp!.Value), 2),
                        AvgSINR = Math.Round(g.Average(x => x.sinr!.Value), 2)
                    })
                    .ToListAsync(ct);

                var bandDistribution = await db.tbl_network_log.AsNoTracking()
                    .Where(x => !string.IsNullOrEmpty(x.band))
                    .GroupBy(x => x.band!)
                    .Select(g => new { band = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToListAsync(ct);

                message.Data = new
                {
                    totalSessions,
                    totalOnlineSessions,
                    totalSamples,
                    totalUsers,
                    totalNetworkTypes = networkTypeDistributionTask.Count,
                    networkTypeDistribution_horizontal_bar = networkTypeDistributionTask,
                    samplesByAlphaLong,
                    monthlySampleCounts,
                    avgRsrpSinrPerOperator_bar = avgRsrpSinr,
                    bandDistribution_pie = bandDistribution
                };
            }
            catch (Exception ex)
            {
                message.Status = 0;
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }

            return Json(message);
        }

        // ========= Graphs (sequential, safe; you can convert to factory+cache similarly) =========
        [HttpGet]
        public async Task<JsonResult> GetDashboardGraphData(CancellationToken ct = default)
        {
            var message = new ReturnAPIResponse();

            try
            {
                cf.SessionCheck();
                message.Status = 1;

                var networkTypeDistribution_horizontal_bar = await db.tbl_network_log.AsNoTracking()
                    .Where(x => x.network != null && x.network != "")
                    .GroupBy(x => x.network!)
                    .Select(g => new { network = g.Key, count = g.Count() })
                    .ToListAsync(ct);

                var avgRsrpSinrPerOperator_bar = await db.tbl_network_log.AsNoTracking()
                    .Where(x => x.rsrp != null && x.m_alpha_long != null && x.m_alpha_long != "")
                    .GroupBy(x => x.m_alpha_long!)
                    .Select(g => new { Operator = g.Key, AvgRSRP = Math.Round(g.Average(x => x.rsrp!.Value), 2) })
                    .OrderByDescending(x => x.AvgRSRP)
                    .ToListAsync(ct);

                var bandDistribution_pie = await db.tbl_network_log.AsNoTracking()
                    .Where(x => !string.IsNullOrEmpty(x.band))
                    .GroupBy(x => x.band!)
                    .Select(g => new { band = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToListAsync(ct);

                var handsetWiseAvg_bar = await (
                    from log in db.tbl_network_log.AsNoTracking()
                    join s in db.tbl_session.AsNoTracking() on log.session_id equals s.id
                    join u in db.tbl_user.AsNoTracking() on s.user_id equals u.id
                    where log.rsrp != null && !string.IsNullOrEmpty(u.make)
                    group log by u.make! into g
                    select new { Make = g.Key, Avg = Math.Round(g.Average(x => x.rsrp!.Value), 2), Samples = g.Count() }
                ).OrderByDescending(x => x.Avg).ToListAsync(ct);

                message.Data = new
                {
                    networkTypeDistribution_horizontal_bar,
                    avgRsrpSinrPerOperator_bar,
                    bandDistribution_pie,
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

        // ========= Users =========
        [HttpPost]
        public async Task<JsonResult> GetAllUsers(int page = 1, int pageSize = 100, CancellationToken ct = default)
        {
            try
            {
                pageSize = Math.Clamp(pageSize, 1, 500);
                var skip = Math.Max(0, (page - 1) * pageSize);

                var users = await db.tbl_user.AsNoTracking()
                    .Where(a => a.isactive == 1)
                    .OrderBy(a => a.name)
                    .Skip(skip)
                    .Take(pageSize)
                    .Select(u => new UserListItemDto
                    {
                        Id = u.id,
                        Name = u.name,
                        Email = u.email,
                        Mobile = u.mobile,
                        UserTypeId = u.m_user_type_id,
                        IsActive = u.isactive,
                        DateCreated = u.date_created,
                        Make = u.make,
                        Model = u.model,
                        Os = u.os,
                        OperatorName = u.operator_name
                    })
                    .ToListAsync(ct);

                return Json(users);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Message = "Error fetching users: " + ex.Message });
            }
        }

        public ActionResult ManageUsers()
        {
            if (!IsAngularRequest())
                return RedirectToAction("Index", "Home");
            if (!cf.SessionCheck("1"))
                return RedirectToAction("Dashboard", "Admin");
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetUsers(string token, string? UserName, string? Email, string? Mobile, int page = 1, int pageSize = 50, CancellationToken ct = default)
        {
            var message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message = cf.MatchToken(token);
                if (message.Status != 1) return Json(message);

                var q = db.tbl_user.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(UserName))
                    q = q.Where(a => a.name != null && EF.Functions.Like(a.name, $"%{UserName}%"));
                if (!string.IsNullOrWhiteSpace(Email))
                    q = q.Where(a => a.email != null && EF.Functions.Like(a.email, $"%{Email}%"));
                if (!string.IsNullOrWhiteSpace(Mobile))
                    q = q.Where(a => a.mobile != null && EF.Functions.Like(a.mobile, $"%{Mobile}%"));

                pageSize = Math.Clamp(pageSize, 1, 200);
                var skip = Math.Max(0, (page - 1) * pageSize);

                var result = await q
                    .OrderBy(a => a.name)
                    .Skip(skip)
                    .Take(pageSize)
                    .Select(u => new
                    {
                        ob_user = new
                        {
                            id = u.id,
                            uid = u.uid,
                            token = u.token,
                            name = u.name,
                            password = !string.IsNullOrEmpty(u.password) ? new string('*', 15) : null,
                            email = u.email,
                            make = u.make,
                            model = u.model,
                            os = u.os,
                            operator_name = u.operator_name,
                            company_id = u.company_id,
                            mobile = u.mobile,
                            isactive = u.isactive,
                            m_user_type_id = u.m_user_type_id,
                            last_login = u.last_login,
                            date_created = u.date_created,
                            device_id = u.device_id,
                            gcm_id = u.gcm_id
                        }
                    })
                    .ToListAsync(ct);

                message.Status = 1;
                message.Data = result;
            }
            catch (Exception ex)
            {
                new Writelog(db).write_exception_log(0, "AdminHomeController", "GetUsers", DateTime.Now, ex);
            }
            return Json(message);
        }

        public ActionResult ManageUser() => View();

        [HttpGet]
        public async Task<JsonResult> GetUserById(string token, int UserID, CancellationToken ct = default)
        {
            var message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message.Status = 1;
                if (message.Status == 1)
                {
                    var user = await db.tbl_user.AsNoTracking()
                        .Where(a => a.isactive == 1 && a.id == UserID)
                        .Select(u => new
                        {
                            id = u.id,
                            uid = u.uid,
                            token = u.token,
                            name = u.name,
                            password = !string.IsNullOrEmpty(u.password) ? new string('*', 15) : null,
                            email = u.email,
                            make = u.make,
                            model = u.model,
                            os = u.os,
                            operator_name = u.operator_name,
                            company_id = u.company_id,
                            mobile = u.mobile,
                            isactive = u.isactive,
                            m_user_type_id = u.m_user_type_id,
                            last_login = u.last_login,
                            date_created = u.date_created,
                            device_id = u.device_id,
                            gcm_id = u.gcm_id
                        })
                        .FirstOrDefaultAsync(ct);

                    message.Data = user;
                }
            }
            catch (Exception ex)
            {
                new Writelog(db).write_exception_log(0, "AdminHomeController", "GetUserById", DateTime.Now, ex);
            }
            return Json(message);
        }

        public static string DecodeFrom64(string encodedData)
        {
            var enc = System.Text.Encoding.UTF8;
            byte[] bytes = Convert.FromBase64String(encodedData);
            return enc.GetString(bytes);
        }

        public static string EncodePasswordToBase64(string password)
        {
            var enc = System.Text.Encoding.UTF8;
            return Convert.ToBase64String(enc.GetBytes(password));
        }

        [HttpPost]
        public async Task<JsonResult> SaveUserDetails([FromForm] IFormCollection values, tbl_user users, string token1, string ip, CancellationToken ct = default)
        {
            var message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message.Status = 1;

                if (message.Status == 1)
                {
                    users.name = users.name != null ? WebUtility.HtmlEncode(users.name) : users.name;
                    users.email = users.email != null ? WebUtility.HtmlEncode(users.email) : users.email;
                    users.mobile = users.mobile != null ? WebUtility.HtmlEncode(users.mobile) : users.mobile;

                    if (users.id == 0)
                    {
                        var exists = await db.tbl_user.AsNoTracking().AnyAsync(a => a.email == users.email && a.isactive == 1, ct);
                        if (!exists)
                        {
                            users.date_created = DateTime.Now;
                            users.isactive = 1;
                            db.tbl_user.Add(users);
                            await db.SaveChangesAsync(ct);
                            message.Status = 1;
                            message.Message = DisplayMessage.UserDetailsSaved;
                        }
                        else
                        {
                            message.Message = DisplayMessage.UserExist;
                        }
                    }
                    else
                    {
                        var getUser = await db.tbl_user.FirstOrDefaultAsync(a => a.id == users.id, ct);
                        if (getUser != null)
                        {
                            getUser.name = users.name;
                            getUser.email = users.email;
                            getUser.mobile = users.mobile;
                            getUser.m_user_type_id = users.m_user_type_id;
                            db.Entry(getUser).State = EntityState.Modified;
                            await db.SaveChangesAsync(ct);
                            message.Status = 2;
                            message.Message = DisplayMessage.UserDetailsUpdated;
                        }
                    }
                    message.token = ""; // cf.CreateToken(ip);
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
        public async Task<JsonResult> GetUser(int UserID, string token, CancellationToken ct = default)
        {
            var message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message = cf.MatchToken(token);
                if (message.Status == 1)
                {
                    var user = await db.tbl_user.AsNoTracking()
                        .Where(a => a.id == UserID)
                        .Select(u => new
                        {
                            id = u.id,
                            uid = u.uid,
                            token = u.token,
                            name = u.name,
                            password = "",
                            email = u.email,
                            make = u.make,
                            model = u.model,
                            os = u.os,
                            operator_name = u.operator_name,
                            company_id = u.company_id,
                            mobile = u.mobile,
                            isactive = u.isactive,
                            m_user_type_id = u.m_user_type_id,
                            last_login = u.last_login,
                            date_created = u.date_created,
                            device_id = u.device_id,
                            gcm_id = u.gcm_id
                        })
                        .FirstOrDefaultAsync(ct);

                    message.Data = user;
                }
            }
            catch (Exception ex)
            {
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }

        [HttpPost]
        public async Task<JsonResult> DeleteUser(int id, string ip, CancellationToken ct = default)
        {
            var message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message.Status = 1;
                if (message.Status == 1)
                {
                    var getUser = await db.tbl_user.FirstOrDefaultAsync(a => a.id == id, ct);
                    if (getUser != null)
                    {
                        getUser.isactive = 2;
                        db.Entry(getUser).State = EntityState.Modified;
                        await db.SaveChangesAsync(ct);
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
        public async Task<JsonResult> UserResetPassword(int userid, string newpwd, string captcha, CancellationToken ct = default)
        {
            var ret = new ReturnMessage();
            try
            {
                var getUser = await db.tbl_user.FirstOrDefaultAsync(a => a.id == userid, ct);
                if (getUser != null)
                {
                    getUser.password = newpwd; // NOTE: consider hashing
                    db.Entry(getUser).State = EntityState.Modified;
                    await db.SaveChangesAsync(ct);
                    ret.Status = 1;
                    ret.Message = "Password has been reset successfully.";
                }
                else
                {
                    ret.Status = 0;
                    ret.Message = "Invalid Request";
                }
            }
            catch (Exception ex)
            {
                ret.Status = 0;
                ret.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(ret);
        }

        [HttpPost]
        public async Task<JsonResult> ChangePassword(int userid, string oldpwd, string newpwd, string captcha, CancellationToken ct = default)
        {
            var ret = new ReturnMessage();
            try
            {
                var captchaText = HttpContext?.Session.GetString("CaptchaImageText");
                if (string.Equals(captchaText, captcha, StringComparison.Ordinal))
                {
                    var getUser = await db.tbl_user.FirstOrDefaultAsync(a => a.id == userid && a.password == oldpwd, ct);
                    if (getUser != null)
                    {
                        getUser.password = newpwd; // NOTE: consider hashing
                        db.Entry(getUser).State = EntityState.Modified;
                        await db.SaveChangesAsync(ct);
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

        // ========= Sessions / Logs =========
        public ActionResult ManageSession()
        {
            if (!IsAngularRequest() || !cf.SessionCheck())
                return RedirectToAction("Index", "Home");
            return View();
        }

        [HttpGet]
        public async Task<JsonResult> GetAllNetworkLogs(int max = 100_000, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
        {
            try
            {
                var q = db.tbl_network_log.AsNoTracking()
                    .Where(log => log.lat != null && log.lon != null);

                if (from.HasValue) q = q.Where(l => l.timestamp >= from);
                if (to.HasValue)   q = q.Where(l => l.timestamp <= to);

                max = Math.Clamp(max, 1_000, 200_000);

                var allLogs = await q
                    .OrderBy(l => l.timestamp)
                    .Take(max)
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
                    .ToListAsync(ct);

                return Json(allLogs);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Message = "An error occurred on the server: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetOperatorCoverageRanking(double min = -95, double max = 0, CancellationToken ct = default)
        {
            try
            {
                var result = await db.tbl_network_log.AsNoTracking()
                    .Where(l => l.rsrp != null && l.m_alpha_long != null && l.rsrp!.Value >= min && l.rsrp!.Value <= max)
                    .GroupBy(l => l.m_alpha_long!)
                    .Select(g => new { name = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToListAsync(ct);

                return Json(result);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Message = "Error: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetOperatorQualityRanking(double min = -10, double max = 0, CancellationToken ct = default)
        {
            try
            {
                var result = await db.tbl_network_log.AsNoTracking()
                    .Where(l => l.rsrq != null && l.m_alpha_long != null && l.rsrq!.Value >= min && l.rsrq!.Value <= max)
                    .GroupBy(l => l.m_alpha_long!)
                    .Select(g => new { name = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToListAsync(ct);

                return Json(result);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Message = "Error: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetSessions(int page = 1, int pageSize = 100, CancellationToken ct = default)
        {
            try
            {
                pageSize = Math.Clamp(pageSize, 1, 500);
                var skip = Math.Max(0, (page - 1) * pageSize);

                var sessions = await (
                    from s in db.tbl_session.AsNoTracking()
                    join u in db.tbl_user.AsNoTracking() on s.user_id equals u.id
                    orderby s.start_time descending
                    select new
                    {
                        id = s.id,
                        session_name = "Session " + s.id,
                        start_time = s.start_time,
                        end_time = s.end_time,
                        notes = s.notes,

                        // keep original types from your entity (no casting)
                        start_lat_raw = s.start_lat,
                        start_lon_raw = s.start_lon,
                        end_lat_raw   = s.end_lat,
                        end_lon_raw   = s.end_lon,
                        capture_frequency_raw = s.capture_frequency,

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
                    .Skip(skip)
                    .Take(pageSize)
                    .ToListAsync(ct);

                return Json(sessions);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new { Message = "An error occurred on the server: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetSessionsByDateRange(string startDateIso, string endDateIso, int maxLogsPerSession = 10_000, CancellationToken ct = default)
        {
            try
            {
                if (!DateTime.TryParse(startDateIso, out DateTime startDate) ||
                    !DateTime.TryParse(endDateIso, out DateTime endDate))
                {
                    return Json(new { success = false, Message = "Invalid date format" });
                }

                endDate = endDate.Date.AddDays(1).AddTicks(-1);

                var sessionsData = await (
                    from s in db.tbl_session.AsNoTracking()
                    join u in db.tbl_user.AsNoTracking() on s.user_id equals u.id
                    where s.start_time.HasValue && s.start_time.Value >= startDate && s.start_time.Value <= endDate
                    orderby s.start_time descending
                    select new
                    {
                        id = s.id,
                        session_name = "Session " + s.id,
                        start_time = s.start_time,
                        end_time = s.end_time,
                        notes = s.notes,

                        // keep original types (no double casts)
                        start_lat_raw = s.start_lat,
                        start_lon_raw = s.start_lon,
                        end_lat_raw   = s.end_lat,
                        end_lon_raw   = s.end_lon,
                        capture_frequency_raw = s.capture_frequency,

                        distance_km = s.distance,
                        start_address = s.start_address,
                        end_address = s.end_address,

                        CreatedBy = u.name,
                        mobile = u.mobile,
                        make = u.make,
                        model = u.model,
                        os = u.os,
                        operator_name = u.operator_name
                    })
                    .ToListAsync(ct);

                var sessionIds = sessionsData.Select(s => s.id).ToList();
                if (sessionIds.Count == 0) return Json(Array.Empty<object>());

                maxLogsPerSession = Math.Clamp(maxLogsPerSession, 100, 200_000);

                var allLogsForSessions = await db.tbl_network_log.AsNoTracking()
                    .Where(log => sessionIds.Contains(log.session_id))
                    .OrderBy(l => l.timestamp)
                    .Select(l => new LogDto
                    {
                        session_id = l.session_id,
                        lat = l.lat,
                        lon = l.lon,
                        rsrp = l.rsrp,
                        rsrq = l.rsrq,
                        sinr = l.sinr,
                        ul_tpt = l.ul_tpt,
                        dl_tpt = l.dl_tpt,
                        band = l.band,
                        network = l.network,
                        m_alpha_long = l.m_alpha_long,
                        timestamp = l.timestamp
                    })
                    .ToListAsync(ct);

                var logsLookup = allLogsForSessions
                    .GroupBy(x => x.session_id)
                    .ToDictionary(g => g.Key, g => g.Take(maxLogsPerSession).ToList());

                var finalResult = sessionsData.Select(s => new
                {
                    s.id,
                    s.session_name,
                    s.start_time,
                    s.end_time,
                    s.notes,
                    s.start_lat_raw,
                    s.start_lon_raw,
                    s.end_lat_raw,
                    s.end_lon_raw,
                    s.capture_frequency_raw,
                    s.distance_km,
                    s.start_address,
                    s.end_address,
                    s.CreatedBy,
                    s.mobile,
                    s.make,
                    s.model,
                    s.os,
                    s.operator_name,
                    Logs = logsLookup.TryGetValue(s.id, out var ls) ? ls : new List<LogDto>()
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
        public async Task<IActionResult> DeleteSession([FromQuery] string id, CancellationToken ct = default)
        {
            try
            {
                if (!int.TryParse(id, out int sessionId))
                    return BadRequest("Invalid session id");

                var session = await db.tbl_session.FindAsync(new object?[] { sessionId }, ct);
                if (session == null)
                {
                    return NotFound(new { success = false, message = "Session not found." });
                }

                var logs = await db.tbl_network_log.Where(l => l.session_id == sessionId).ToListAsync(ct);
                if (logs.Count > 0) db.tbl_network_log.RemoveRange(logs);
                db.tbl_session.Remove(session);
                await db.SaveChangesAsync(ct);

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
    }
}
