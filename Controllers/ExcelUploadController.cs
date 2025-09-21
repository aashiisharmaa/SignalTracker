using SignalTracker.Helper;
using SignalTracker.Models;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using static System.Net.WebRequestMethods;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace SignalTracker.Controllers
{
    public class ExcelUploadController : BaseController
    {
        ApplicationDbContext db = null;
        CommonFunction cf = null;
        private static TimeZoneInfo INDIAN_ZONE = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        public ExcelUploadController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            db = context;
            cf = new CommonFunction(context, httpContextAccessor);
        }
        public ActionResult Index()
        {
            if (!IsAngularRequest() || !cf.SessionCheck())
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }
        
        [HttpGet]
        public IActionResult DownloadExcel(int FileType, string fileName)
        {          
            var filePath = "";
            if(FileType == 0)
                filePath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedExcels", fileName);
            else
            {
                fileName = Constant.TempFiles[FileType];
                filePath = Path.Combine(Directory.GetCurrentDirectory(), "Template-Files", fileName);
            }

            if (System.IO.File.Exists(filePath))
            {
                var fileBytes = System.IO.File.ReadAllBytes(filePath);
                var contentType = CommonFunction.GetMimeType(filePath);
                return File(fileBytes, contentType, fileName);
            }
            else
                return Json(new { status = 0, message = "Template not found" });
        }
        
        [HttpGet]
        public JsonResult GetUploadedExcelFiles(string token, int FileType)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message.Status = 1;
                if (message.Status == 1)
                {
                    var GetObj = (from ob_excel in db.tbl_upload_history
                                  join ob_user in db.tbl_user on ob_excel.uploaded_by equals ob_user.id                                 
                                  select new
                                  {
                                      id = ob_excel.id,
                                      file_type = ob_excel.file_type,
                                      file_name = ob_excel.file_name,
                                      uploaded_on = ob_excel.uploaded_on,
                                      uploaded_by = ob_user.name,
                                      uploaded_id = ob_excel.uploaded_by,
                                      status = ob_excel.status == 1 ? "Success" : "Failed",
                                      remarks = ob_excel.remarks,                                      
                                  }).Where(a => a.file_type == FileType && (a.uploaded_id == cf.UserId)).OrderByDescending(a => a.id).ToList().Take(20);

                    message.Data = GetObj;
                }
            }
            catch (Exception ex)
            {
                Writelog writelog = new Writelog(db);
                writelog.write_exception_log(0, "AdminHomeController", "GetUploadedExcelFiles", DateTime.Now, ex);
                message.Status = 0;
                message.Message = DisplayMessage.ErrorMessage;
            }
            return Json(message);
        }
        
        
       [HttpPost]
[RequestSizeLimit(100_000_000)]
public JsonResult UploadExcelFile([FromForm] IFormCollection values)
{
    ReturnAPIResponse message = new ReturnAPIResponse();
    Writelog writelog = new Writelog(db);
    string logMessage = "";
    int step = 0;

    try
    {
        step = 1;
        logMessage = "Method started";
        writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);

        // Safe form value access
        step = 2;
        string Remarks = GetFormValue(values, "remarks");
        string token = GetFormValue(values, "token");
        string ip = GetFormValue(values, "ip");
        string ProjectName = GetFormValue(values, "ProjectName");
        string SessionIds = GetFormValue(values, "SessionIds");

        step = 3;
        int UploadFileType = 0;
        string uploadFileTypeValue = GetFormValue(values, "UploadFileType");
        if (!string.IsNullOrEmpty(uploadFileTypeValue))
        {
            int.TryParse(uploadFileTypeValue, out UploadFileType);
        }

        logMessage = $"Form values extracted - Remarks: {Remarks}, ProjectName: {ProjectName}, UploadFileType: {UploadFileType}";
        writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);

        step = 4;
        cf.SessionCheck();
        message.Status = 1;

        step = 5;
        if (message.Status == 1)
        {
            logMessage = "Session check passed";
            writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);

            step = 6;
            var remarksValidation = InputValidator.ValidateRemarks(Remarks, "Remarks");
            if (!remarksValidation.isValid)
            {
                message.Status = 0;
                message.Message = remarksValidation.errorMessage;
                return Json(message);
            }
            Remarks = remarksValidation.sanitized;

            step = 7;
            if (values.Files == null)
            {
                logMessage = "values.Files is null";
                writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);
                message.Status = 0;
                message.Message = "No files found in request.";
                return Json(message);
            }

            step = 8;
            if (values.Files.Count == 0)
            {
                logMessage = "values.Files.Count is 0";
                writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);
                message.Status = 0;
                message.Message = "Please select a file to upload.";
                return Json(message);
            }

            step = 9;
            int UserID = 0;
            if (HttpContext != null)
            {
                logMessage = "HttpContext is not null";
                writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);

                if (HttpContext.Session != null)
                {
                    logMessage = "HttpContext.Session is not null";
                    writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);

                    var userIdValue = HttpContext.Session.GetInt32("UserID");
                    if (userIdValue.HasValue)
                    {
                        UserID = userIdValue.Value;
                        logMessage = $"UserID extracted: {UserID}";
                        writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);
                    }
                    else
                    {
                        logMessage = "UserID is null in session";
                        writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);
                    }
                }
                else
                {
                    logMessage = "HttpContext.Session is null";
                    writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);
                }
            }
            else
            {
                logMessage = "HttpContext is null";
                writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);
            }

            step = 10;
            var FileUpload1 = values.Files["UploadFile"];
            if (FileUpload1 == null)
            {
                logMessage = "FileUpload1 is null";
                writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);
                message.Status = 0;
                message.Message = "UploadFile is required.";
                return Json(message);
            }

            step = 11;
            if (FileUpload1.Length == 0)
            {
                logMessage = "FileUpload1.Length is 0";
                writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);
                message.Status = 0;
                message.Message = "UploadFile is empty.";
                return Json(message);
            }

            step = 12;
            int MaxContent = (int)FileUpload1.Length;
            float SizeInKB = MaxContent / 1024;
            
            logMessage = $"FileUpload1 - Name: {FileUpload1.FileName}, Size: {SizeInKB} KB";
            writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);

            step = 13;
            if (SizeInKB > 0)
            {
                var directorypath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedExcels");
                string contentType = FileUpload1.ContentType;
               
                logMessage = $"ContentType: {contentType}";
                writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);

                step = 14;
                if (CommonFunction.ValidateCSVZipFile(contentType))
                {
                    DateTime Date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, INDIAN_ZONE);
                    
                    step = 15;
                    if (!Directory.Exists(directorypath))
                    {
                        Directory.CreateDirectory(directorypath);
                    }
                    
                    step = 16;
                    string inboundPolygonFile = "";
                    var FileUpload2 = values.Files["UploadNoteFile"];
                    
                    if (FileUpload2 != null && FileUpload2.Length > 0)
                    {
                        logMessage = $"FileUpload2 found - Name: {FileUpload2.FileName}, Size: {FileUpload2.Length}";
                        writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);

                        string extension1 = Path.GetExtension(FileUpload2.FileName);
                        MaxContent = (int)FileUpload2.Length;
                        SizeInKB = MaxContent / 1024;
                        
                        if (SizeInKB > 0 && (CommonFunction.ValidateCSVZipFile(FileUpload2.ContentType) || CommonFunction.ValidateGeoJsonFile(FileUpload2.ContentType)))
                        {
                            inboundPolygonFile = "Polygon_" + Date.ToString("MMddyyyyHmmss") + extension1;
                        }
                        else
                        {
                            message.Status = 0;
                            message.Message = "Please upload valid inbound polygon zip/csv file.";
                            return Json(message);
                        }
                    }

                    step = 17;
                    string extension = Path.GetExtension(FileUpload1.FileName);
                    string file_name = "File_" + Date.ToString("MMddyyyyHmmss") + extension;

                    string path = Path.Combine(directorypath, file_name);
                    
                    logMessage = $"Saving file to: {path}";
                    writelog.write_info_log(0, "UploadExcelFile", $"Step {step}: {logMessage}", DateTime.Now);

                    using (var stream = new FileStream(path, FileMode.Create))
                    {
                        FileUpload1.CopyTo(stream);
                    }
                    
                    // ... rest of your processing code with similar logging

                    message.Status = 1;
                    message.Message = "File processed successfully up to step " + step;
                }
                else
                {
                    message.Status = 0;
                    message.Message = "Invalid file type: " + contentType;
                }
            }
            else
            {
                message.Status = 0;
                message.Message = "File size should be greater than 0KB.";
            }
        }
    }
    catch (Exception ex)
    {
        logMessage = $"Exception at step {step}: {ex.Message}";
        if (ex.InnerException != null)
        {
            logMessage += $" | Inner: {ex.InnerException.Message}";
        }
        logMessage += $" | StackTrace: {ex.StackTrace}";
        
        writelog.write_exception_log(0, "UploadExcelFile", logMessage, DateTime.Now, ex);
        
        message.Status = 0;
        message.Message = $"Error at step {step}: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message);
    }
    finally
    {
        writelog.write_info_log(0, "UploadExcelFile", $"Method completed with status: {message.Status}", DateTime.Now);
    }
    
    return Json(message);
}
        private string GetFormValue(IFormCollection values, string key)
        {
            if (values == null) return string.Empty;
            
            if (values.ContainsKey(key))
            {
                var value = values[key];
                if (!string.IsNullOrEmpty(value))
                {
                    return value.ToString();
                }
            }
            return string.Empty;
        }

        [HttpGet]
        public JsonResult GetSessions(DateTime fromDate, DateTime toDate)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                message.Status = 1;

                var rawSessions = db.tbl_session.Where(s => s.start_time >= fromDate && s.end_time <= toDate)
                               .Join(db.tbl_user,
                                   s => s.user_id,
                                   u => u.id,
                                   (s, u) => new
                                   {
                                       s.id,
                                       s.start_time,
                                       s.notes,
                                       s.start_address,
                                       userName = u.name
                                   })
                             .ToList();

                var formattedSessions = rawSessions.Select(x => new
                {
                    id = x.id,
                    label = $"{x.userName} {(x.start_time == null ? "" : x.start_time.Value.ToString("dd MMM yyyy hh:mm tt"))} {x.notes} {x.start_address}"
                }).ToList();

                message.Data = formattedSessions;
            }
            catch (Exception ex)
            {
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }
    }
}