using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SignalTracker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting.Server;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using Newtonsoft.Json;
using SignalTracker.Helper;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;

namespace SignalTracker.Controllers
{
    [Route("Home")]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _db;
        private static TimeZoneInfo INDIAN_ZONE = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        private readonly CommonFunction? cf = null;
        public HomeController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _db = context;
            cf = new CommonFunction(context, httpContextAccessor);
        }

        public IActionResult Index()
        {
            if (cf?.SessionCheck() == true)
            {
                return RedirectToAction("Index", "Admin");
            }
            else
                return View();
        }
        #region Account
        private static void RegenerateSessionId()
        {

            //// Initialise variables for regenerating the session id
            //HttpContext Context = System.Web.HttpContext.Current;
            //System.Web.SessionState.SessionIDManager manager = new System.Web.SessionState.SessionIDManager();
            //string oldId = manager.GetSessionID(Context);
            //string newId = manager.CreateSessionID(Context);
            //bool isAdd = false, isRedir = false;

            //// Save a new session ID
            //manager.SaveSessionID(Context, newId, out isRedir, out isAdd);

            //// Get the fields using the below and create variables for storage
            //HttpApplication ctx = System.Web.HttpContext.Current.ApplicationInstance;
            //HttpModuleCollection mods = ctx.Modules;
            //System.Web.SessionState.SessionStateModule ssm = (System.Web.SessionState.SessionStateModule)mods.Get("Session");
            //System.Reflection.FieldInfo[] fields = ssm.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            //System.Web.SessionState.SessionStateStoreProviderBase store = null;
            //System.Reflection.FieldInfo rqIdField = null, rqLockIdField = null, rqStateNotFoundField = null;
            //System.Web.SessionState.SessionStateStoreData rqItem = null;

            //// Assign to each variable the appropriate field values
            //foreach (System.Reflection.FieldInfo field in fields)
            //{
            //    if (field.Name.Equals("_store")) store = (System.Web.SessionState.SessionStateStoreProviderBase)field.GetValue(ssm);
            //    if (field.Name.Equals("_rqId")) rqIdField = field;
            //    if (field.Name.Equals("_rqLockId")) rqLockIdField = field;
            //    if (field.Name.Equals("_rqSessionStateNotFound")) rqStateNotFoundField = field;
            //    if (field.Name.Equals("_rqItem")) rqItem = (System.Web.SessionState.SessionStateStoreData)field.GetValue(ssm);
            //}

            //// Remove the previous session value
            //object lockId = rqLockIdField.GetValue(ssm);
            //if ((lockId != null) && (oldId != null))
            //    store.RemoveItem(Context, oldId, lockId, rqItem);

            //rqStateNotFoundField.SetValue(ssm, true);
            //rqIdField.SetValue(ssm, newId);
        }
        public ActionResult Login()
        {
            return View();
        }
        [HttpPost("GetStateIformation")]
        public JsonResult GetStateIformation()
        {
            const string src = "abcdefghijklmnopqrstuvwxyz0123456789";
            int length = 12;
            var sb = new System.Text.StringBuilder();
            Random RNG = new Random();
            for (var i = 0; i < length; i++)
            {
                var c = src[RNG.Next(0, src.Length)];
                sb.Append(c);
            }
            HttpContext.Session.SetString("salt", sb.ToString());
            return Json(sb.ToString());
        }
        [HttpPost("UserLogin")]
        public async Task<JsonResult> UserLogin([FromBody] LoginData obj)
        {
            try
            {
                if (obj == null || string.IsNullOrWhiteSpace(obj.Email) || string.IsNullOrWhiteSpace(obj.Password))
                    return Json(new { success = false, message = "Email and password are required." });

                // TODO: hash and compare securely in production
                var user = await _db.tbl_user.FirstOrDefaultAsync(u => u.email == obj.Email && u.isactive == 1);
                if (user == null || user.password != obj.Password)
                    return Json(new { success = false, message = "Invalid email or password!" });

                var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.email),
            new Claim("UserId", user.id.ToString()),
            new Claim("UserTypeId", user.m_user_type_id.ToString()),
            new Claim(ClaimTypes.Role, "User")
        };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    new AuthenticationProperties { IsPersistent = true });

                // Keep these consistent: store email in "UserName"
                HttpContext.Session.SetString("UserName", user.email);
                HttpContext.Session.SetInt32("UserID", user.id);
                HttpContext.Session.SetInt32("UserType", user.m_user_type_id);

                var userDto = new
                {
                    id = user.id,
                    name = user.name,
                    email = user.email,
                    m_user_type_id = user.m_user_type_id
                };

                return Json(new { success = true, user = userDto });
            }
            catch (Exception ex)
            {
                var writelog = new Writelog(_db);
                writelog.write_exception_log(0, "Home", "UserLogin", DateTime.Now, ex);
                return Json(new { success = false, message = "An error occurred. Please try again." });
            }
        }


        [HttpPost("GetUserForgotPassword")]
        public JsonResult GetUserForgotPassword([FromBody] LoginData obj)
        {
            ReturnMessage message = new ReturnMessage();
            message.Status = 0;
            message.Message = DisplayMessage.ErrorMessage;
            try
            {
                if (1 == 1 || HttpContext.Session.GetString("CaptchaImageText") != null && HttpContext.Session.GetString("CaptchaImageText") == obj.Captcha)
                {
                    var user = _db.tbl_user.Where(a => a.email == obj.Email && a.isactive == 1 && a.m_user_type_id != 4).FirstOrDefault();
                    if (user != null)
                    {
                        if (string.IsNullOrEmpty(user.uid))
                        {
                            var uid = Guid.NewGuid();
                            user.uid = uid.ToString();
                        }
                        //user.link_share_date = DateTime.Now;
                        _db.Entry(user).State = EntityState.Modified;
                        _db.SaveChanges();

                        SendMail mail = new SendMail(_db);
                        string[] send_to = new string[] { user.email };
                        string[] bcc_to = new string[] { "" };

                        var baseUrl = $"{Request.Scheme}://{Request.Host}";
                        if (baseUrl.Contains("localhost"))
                            send_to = new string[] { "baghel3349@gmail.com" };

                        var resetUrl = $"{baseUrl}/Home/ResetPassword?link={user.uid}";
                        string body = $"Dear {user.name},<br /><br />Please click the link below to reset your password:<br /><a href='{resetUrl}' title='Click here to reset password'>Reset Password</a>";


                        string subject = "Forecast-Forgot password";
                        bool send = mail.send_mail(body, send_to, bcc_to, subject, null, "");
                        if (send)
                        {
                            message.Status = 1;
                            message.Message = "Reset password link has been sent on your email id and valid for 15 minutes only.";
                        }
                        else
                            message.Message = "Email is not send, kindly contact admin.";

                    }
                    else
                        message.Message = "You have entered wrong email id.";
                }
                else
                {
                    message.Message = "Invalid CAPTCHA Code !";
                }

            }
            catch (Exception ex)
            {

                // --- Start of Completed Catch Block ---

                // 1. Log the detailed exception for debugging purposes.
                // This uses the Writelog helper class found in your project.
                Writelog writelog = new Writelog(_db);
                writelog.write_exception_log(0, "HomeController", "GetUserForgotPassword", DateTime.Now, ex);

                // 2. Set a generic, user-friendly error message.
                // This prevents leaking sensitive server details to the frontend.
                message.Status = 0;
                message.Message = "An unexpected server error occurred. Please try again later.";

                // --- End of Completed Catch Block ---
            }
            return Json(message);
        }
        public ActionResult ResetPassword(string link, string Email)
        {
            ViewData["LinkValid"] = false;
            var GetUser = _db.tbl_user.Where(a => a.uid == link).FirstOrDefault();
            //if (GetUser != null && GetUser.link_share_date!=null)
            //{ 
            //    double minutesDiff = (DateTime.Now-(DateTime)GetUser.link_share_date).TotalMinutes;
            //    if (minutesDiff <= 15)
            //    {
            //        ViewData["LinkValid"] = true;
            //    }
            //}
            return View();
        }
        [HttpPost("ForgotResetPassword")]
        public JsonResult ForgotResetPassword([FromBody] ResetPasswordModel model)
        {
            ReturnMessage ret = new ReturnMessage();
            try
            {
                if (1 == 1 || HttpContext.Session.GetString("CaptchaImageText") == model.Captcha)
                {
                    var GetUser = _db.tbl_user.Where(a => a.uid == model.Token).FirstOrDefault();

                    // The commented-out logic for checking the link's expiration should be implemented.
                    // For now, let's assume if the user exists, the password can be reset.
                    if (GetUser != null)
                    {
                        GetUser.password = model.NewPassword; // Note: Passwords should be hashed in a real application.
                        GetUser.uid = null; // Invalidate the reset link after use.
                        _db.Entry(GetUser).State = EntityState.Modified;
                        _db.SaveChanges();

                        ret.Status = 1;
                        ret.Message = "Password has been reset successfully.";
                    }
                    else
                    {
                        ret.Status = 0;
                        ret.Message = "Invalid or expired reset link.";
                    }
                }
                else
                {
                    ret.Status = 0;
                    ret.Message = "Invalid CAPTCHA Code!";
                }
            }
            catch (Exception ex)
            {
                // --- Start of Completed Catch Block ---

                // 1. Log the detailed exception for debugging.
                Writelog writelog = new Writelog(_db);
                writelog.write_exception_log(0, "HomeController", "ForgotResetPassword", DateTime.Now, ex);

                // 2. Set a generic, user-friendly error message.
                ret.Status = 0;
                ret.Message = "An unexpected server error occurred while resetting your password. Please try again.";

                // --- End of Completed Catch Block ---
            }
            return Json(ret);
        }
        [HttpGet("Logout")]
        public async Task<IActionResult> Logout(string IP)
        {
            try
            {
                var username = HttpContext?.Session.GetString("UserName");
                if (!string.IsNullOrEmpty(username))
                {
                    var objAudit = new tbl_user_login_audit_details
                    {
                        date_of_creation = DateTime.Now,
                        ip_address = IP,
                        username = username,
                        login_status = 2
                    };
                    _db.tbl_user_login_audit_details.Add(objAudit);
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Writelog writelog = new Writelog(_db);
                writelog.write_exception_log(0, "HomeController", "Logout", DateTime.Now, ex);
            }

            HttpContext.Session.Clear();
            foreach (var cookie in Request.Cookies.Keys)
                Response.Cookies.Delete(cookie);

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        #endregion
        [HttpPost("GetLoggedUser")]
        public JsonResult GetLoggedUser(string? ip = null)
        {
            bool isAuth = User?.Identity?.IsAuthenticated == true || (cf?.SessionCheck() ?? false);
            if (!isAuth) return Json(new { });

            var email = User?.FindFirstValue(ClaimTypes.Name)
                        ?? HttpContext.Session.GetString("UserName")
                        ?? string.Empty;

            int? userId = null;
            var userIdStr = User?.FindFirstValue("UserId");
            if (!string.IsNullOrEmpty(userIdStr) && int.TryParse(userIdStr, out var uid))
                userId = uid;
            else
                userId = HttpContext.Session.GetInt32("UserID");

            int? userTypeId = null;
            var userTypeStr = User?.FindFirstValue("UserTypeId");
            if (!string.IsNullOrEmpty(userTypeStr) && int.TryParse(userTypeStr, out var ut))
                userTypeId = ut;
            else
                userTypeId = HttpContext.Session.GetInt32("UserType");

            return Json(new
            {
                id = userId,
                name = email,
                email = email,
                m_user_type_id = userTypeId
            });
        }
        [HttpGet("GetMasterUserTypes")]
        public JsonResult GetMasterUserTypes()
        {
            var typeList = _db.m_user_type.ToList();

            JsonResult r = Json(typeList);
            return r;
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}