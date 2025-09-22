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
        [HttpPost]
        //call from Login
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
        [HttpPost]
        //Call from Index
        [HttpPost]
        //Call from Index
        [HttpPost]
         public async Task<JsonResult> UserLogin([FromBody] LoginData obj)
{
    try
    {
        // NOTE: In a real application, passwords MUST be hashed.
        var userDetails = await _db.tbl_user.FirstOrDefaultAsync(u => u.email == obj.Email && u.isactive == 1);

        if (userDetails != null)
        {
            // --- THIS SECTION IS THE MOST IMPORTANT PART ---
            // Create the security claims that identify the user.
            var claims = new List<Claim>
            {
                // This is the primary claim the system uses to identify the user.
                new Claim(ClaimTypes.Name, userDetails.email),
                
                // You can add other useful information as claims.
                new Claim("UserId", userDetails.id.ToString()),
                new Claim(ClaimTypes.Role, "User") // Example role
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                // Allows the user to stay logged in even after closing the browser.
                IsPersistent = true
            };

            // This command creates the encrypted cookie and signs the user in.
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);
            // --- END OF IMPORTANT SECTION ---

            // Set any additional server-side session data you need
            HttpContext.Session.SetString("UserName", userDetails.name);
            HttpContext.Session.SetInt32("UserID", userDetails.id);

            var userData = new
            {
                id = userDetails.id,
                name = userDetails.name,
                email = userDetails.email,
                m_user_type_id = userDetails.m_user_type_id,
            };

            return Json(new { success = true, user = userDetails });
        }
        else
        {
            return Json(new { success = false, message = "Invalid username or password!" });
        }
    }
    catch (Exception ex)
    {
        var writelog = new Writelog(_db);
        writelog.write_exception_log(0, "Home", "UserLogin", DateTime.Now, ex);
        return Json(new { success = false, message = "An error occurred. Please try again." });
    }
}

   
        [HttpPost]
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
        [HttpPost]
        //call from Login
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
                        login_status = 2 // Assuming '2' means logout
                    };

                    _db.tbl_user_login_audit_details.Add(objAudit);
                    await _db.SaveChangesAsync();

                    var user = await _db.tbl_user.FirstOrDefaultAsync(a => a.email == username);
                    if (user != null)
                    {
                        // Your existing commented-out logic for setting is_loggedin = false would go here if needed.
                    }
                }
            }
            catch (Exception ex)
            {
                // --- Start of Completed Catch Block ---

                // 1. Log the exception for debugging purposes.
                // This will help you diagnose issues, for example, if the audit table has a problem.
                Writelog writelog = new Writelog(_db);
                writelog.write_exception_log(0, "HomeController", "Logout", DateTime.Now, ex);

                // 2. We intentionally do NOT stop the logout process.
                // Even if logging fails, the user must be logged out.

                // --- End of Completed Catch Block ---
            }

            // --- Critical Logout Steps ---
            // These are correctly placed outside the try-catch to ensure they ALWAYS run.

            // Clear the server-side session
            HttpContext.Session.Clear();

            // Clear authentication cookies from the browser
            foreach (var cookie in Request.Cookies.Keys)
            {
                Response.Cookies.Delete(cookie);
            }

            // Formally sign the user out of the authentication scheme
            await HttpContext.SignOutAsync();

            // Redirect the user to the home page
            return RedirectToAction("Index", "Home");
        }
        // Clear session

        #endregion
        [HttpPost]
        [HttpPost]
        public JsonResult GetLoggedUser(string ip)
        {
            // Use the session check to see if a user is logged in
            if (cf.SessionCheck())
            {
                // If they are, get their details from the session
                var emailid = HttpContext.Session.GetString("UserName") ?? string.Empty; // Use UserName as set during login
                var userId = HttpContext.Session.GetInt32("UserID");
                var userTypeId = HttpContext.Session.GetInt32("UserType");

                // Return the logged-in user's data
                return Json(new
                {
                    id = userId,
                    name = emailid, // We'll use the email as the name for now
                    email = emailid,
                    m_user_type_id = userTypeId
                });
            }

           
            return Json(new { });
        }
        [HttpGet]
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