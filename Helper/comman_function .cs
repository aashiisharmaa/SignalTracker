using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using SignalTracker.Models;
using System.Text;
//using System.Web.Security;
using Newtonsoft.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Rendering;
using SignalTracker.Helper;
using SignalTracker;
using Microsoft.AspNetCore.Http;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Net.Mime;

public class CommonFunction
{
    private readonly ApplicationDbContext db;
    private readonly IHttpContextAccessor _httpContextAccessor;
    public int UserId = 0;
    public int UserType = 0;
    public string UserName = "";

    public CommonFunction(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
    {
        db = context;
        _httpContextAccessor = httpContextAccessor;
        if (_httpContextAccessor != null && _httpContextAccessor.HttpContext!=null)
        {
            var uId= _httpContextAccessor.HttpContext.Session.GetInt32("UserID");
            var uType = _httpContextAccessor.HttpContext.Session.GetString("UserType");
            var uName = _httpContextAccessor.HttpContext.Session.GetString("UserName");

            if (uId != null) UserId=(int)uId;
            if (!string.IsNullOrEmpty(uType)) UserType = Convert.ToInt32(uType);
            if (!string.IsNullOrEmpty(uName)) _ = uName;
        }
    }

    public bool SessionCheck( string userType="")
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext !=null && httpContext.User!=null && httpContext.User.Identity!=null && httpContext.User.Identity.IsAuthenticated)
        {
            var userEmail = httpContext.User.Identity.Name;
            var user = db.tbl_user.FirstOrDefault(u => u.email == userEmail && u.isactive == 1);

            if (user != null) /*&& user.is_loggedin*/
            {
                httpContext.Session.SetString("Email", user.email);
                httpContext.Session.SetString("UserName", user.name);
                httpContext.Session.SetInt32("UserID", user.id);
                httpContext.Session.SetString("UserType", user.m_user_type_id.ToString());
                httpContext.Session.SetString("LastLogin", user.last_login==null?"":((DateTime)user.last_login).ToString("dd MMM hh:mm tt"));

                UserType = user.m_user_type_id;
                if(!string.IsNullOrEmpty(userType))
                {
                    if(userType.Contains("" + UserType))
                        return true;
                    else return false;
                }
                return true;
            }
        }
        return false;
    }

    public string CreateToken(string ip)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var email = httpContext.Session.GetString("Email");
        if (!string.IsNullOrEmpty(email) && SessionCheck())
        {
            var tokenData = $"{httpContext.Session.GetInt32("UserID")}#{httpContext.Session.GetString("UserType")}#{ip}#{DateTime.Now:MM-dd-yyyy HH:mm:ss}";
            var token = AESEncrytDecry.Encrypt(tokenData);
            httpContext.Session.SetString("token", token);

            var user = db.tbl_user.FirstOrDefault(u => u.email == email);
            if (user != null)
            {
                user.token = token;
                db.SaveChanges();
            }
            return token;
        }
        return string.Empty;
    }
    public ReturnAPIResponse MatchToken(string token)
    {
        ReturnAPIResponse objRet = new ReturnAPIResponse();
        objRet.Status = 0;
        objRet.Message = "Invalid token, kindly login again.";
        var httpContext = _httpContextAccessor.HttpContext;

        if (SessionCheck())
        {
            

            string ServerToken = httpContext.Session.GetString("token") + "";
            string user_type = httpContext.Session.GetString("UserType") + "";
            if (!string.IsNullOrEmpty(user_type))
            {
                objRet.UserType = Convert.ToInt32(user_type);
            }
            if (string.IsNullOrEmpty(ServerToken))
            {
                string Email = httpContext.Session.GetString("Email") + "";
                if (!string.IsNullOrEmpty(Email))
                {
                    var user_details = db.tbl_user.Where(a => a.email == Email).FirstOrDefault();
                    if (user_details != null)
                    {
                        ServerToken = user_details.token;
                        objRet.UserType = user_details.m_user_type_id;
                    }
                }
                else
                {
                    objRet.Status = 3;
                    objRet.Message = "Your session has been expired, kindly login again.";
                }
            }
            if (!string.IsNullOrEmpty(ServerToken) && !string.IsNullOrEmpty(token))
            {
                string ClientToken = AESEncrytDecry.Decrypt(token);
                ServerToken = AESEncrytDecry.Decrypt(ServerToken);
                string[] ctn = ClientToken.Split('#');
                string[] stn = ServerToken.Split('#');
                if (ctn.Length == stn.Length)
                {
                    for (int i = 0; i < ctn.Length; i++)
                    {
                        objRet.Status = 1;
                        if (ctn[i] != stn[i])
                        {
                            objRet.Message = "Token missmatch or invalid user.";
                            break;
                        }

                    }
                }
            }
        }
        else
        {
            httpContext.Session.Clear();
        }
        return objRet;
    }
    
    

    public static string GetMimeType(string filePath)
    {
        var mimeTypes = new Dictionary<string, string>
    {
       { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { ".xls", "application/vnd.ms-excel" },
        { ".csv", "text/csv" },
        { ".pdf", "application/pdf" },
        { ".txt", "text/plain" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".bmp", "image/bmp" },
        { ".webp", "image/webp" },
        { ".svg", "image/svg+xml" },
        { ".doc", "application/msword" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".ppt", "application/vnd.ms-powerpoint" },
        { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" }
    };

        var ext = Path.GetExtension(filePath).ToLower();
        return mimeTypes.ContainsKey(ext) ? mimeTypes[ext] : "application/octet-stream";
    }
    public static bool IsValidImageFormat(String path)
    {
        using (FileStream fs = File.OpenRead(path))
        {
            byte[] header = new byte[10];
            fs.Read(header, 0, 10);

            foreach (var pattern in new byte[][] {
                    Encoding.ASCII.GetBytes("BM"),
                    Encoding.ASCII.GetBytes("GIF"),
                    new byte[] { 137, 80, 78, 71 },     // PNG
                    new byte[] { 73, 73, 42 },          // TIFF
                    new byte[] { 77, 77, 42 },          // TIFF
                    new byte[] { 255, 216, 255, 224 },  // jpeg
                    new byte[] { 255, 216, 255, 225 }   // jpeg canon
            })
            {
                if (pattern.SequenceEqual(header.Take(pattern.Length)))
                    return true;
            }
        }

        return false;
    }

    public bool CompareFy(string FilledFy, string caomareFy)
    {
        bool isGreater = false;
        try
        {
            if (string.IsNullOrEmpty(FilledFy) || string.IsNullOrEmpty(caomareFy))
                return false;
            string[] a = FilledFy.Split('-');
            string[] b = caomareFy.Split('-');
            if (a.Length == 2 && b.Length == 2)
            {
                int aa = 0, bb = 0;
                int.TryParse(a[1], out aa);
                int.TryParse(b[1], out bb);
                if (bb >= aa)
                    isGreater = true;
            }
        }
        catch (Exception ex)
        {

        }
        return isGreater;
    }
    public string GetPrevFY_OftheSelectedFY(string fy)
    {
        string prevFY = "";
        if (fy == "2016-17")
            prevFY = "2015-16";
        else if (fy == "2017-18")
            prevFY = "2016-17";
        else if (fy == "2018-19")
            prevFY = "2017-18";
        else if (fy == "2019-20")
            prevFY = "2018-19";
        else if (fy == "2020-21")
            prevFY = "2019-20";
        else if (fy == "2021-22")
            prevFY = "2020-21";
        else if (fy == "2022-23")
            prevFY = "2021-22";

        return prevFY;
    }
    public List<string> getLastFyYear(string fy, int fyCount)
    {
        List<string> result = new List<string>();
        if (!string.IsNullOrEmpty(fy))
            result.Add(fy);
        for (int i = 0; i < fyCount; i++)
        {
            string pevfy = GetPrevFY_OftheSelectedFY(fy);
            if (string.IsNullOrEmpty(pevfy))
                break;
            result.Add(pevfy);
            fy = pevfy;
        }
        List<string> OrderList = new List<string>();
        for (int i = result.Count - 1; i >= 0; i--)
        {
            string ofy = result[i];
            OrderList.Add(ofy);
        }
        return OrderList;
    }
    public static bool ValidateCSVZipFile(string contentType)
    {
        if (contentType == "text/csv" || contentType == "application/csv" || contentType == "application/vnd.ms-excel" || contentType == "application/octet-stream" || contentType == "application/zip" || contentType == "application/x-zip-compressed")
            return true;
        else
            return false;
    }
    public static bool ValidateGeoJsonFile(string contentType)
    {
        return contentType == "application/geo+json" ||        // standard MIME type for GeoJSON
               contentType == "application/json" ||            // often used when content type is not specific
               contentType == "text/json" ||                   // legacy/alternate JSON MIME
               contentType == "application/octet-stream";      // fallback (e.g., from some browsers/tools)
    }
}