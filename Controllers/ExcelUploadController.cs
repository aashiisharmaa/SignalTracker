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
        private readonly ApplicationDbContext db;
    private readonly CommonFunction cf;
        // ApplicationDbContext db = null;
        // CommonFunction cf = null;
        private static TimeZoneInfo INDIAN_ZONE = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        // public ExcelUploadController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
         public ExcelUploadController(ApplicationDbContext context, CommonFunction commonFunction)
        {
            this.db = context;
        this.cf = commonFunction;
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
            if (FileType == 0)
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



        // In SignalTracker/Controllers/ExcelUploadController.cs

[HttpPost]
[RequestSizeLimit(100_000_000)]
public JsonResult UploadExcelFile([FromForm] IFormCollection values)
{
    ReturnAPIResponse message = new ReturnAPIResponse();
    try
    {
        // --- Get form data ---
        string Remarks = values["remarks"].ToString();
        int UploadFileType = Convert.ToInt32(values["UploadFileType"]);
        int UserID = HttpContext.Session.GetInt32("UserID") ?? 0;
        string ProjectName = values["ProjectName"].ToString();
        string SessionIds = values["SessionIds"].ToString();

        // --- Validate main file ---
        var mainFile = values.Files["UploadFile"];
        if (mainFile == null || mainFile.Length == 0)
        {
            message.Status = 0;
            message.Message = "Please select a primary file to upload.";
            return Json(message);
        }

        // --- Setup Paths ---
        var directorypath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedExcels");
        if (!Directory.Exists(directorypath))
        {
            Directory.CreateDirectory(directorypath);
        }

        DateTime date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, INDIAN_ZONE);
        string mainFileName = "File_" + date.ToString("MMddyyyyHmmss") + Path.GetExtension(mainFile.FileName);
        string mainFilePath = Path.Combine(directorypath, mainFileName);

        // --- Save the main file ---
        using (var stream = new FileStream(mainFilePath, FileMode.Create))
        {
            mainFile.CopyTo(stream);
        }

        // --- ✨ KEY FIX: Safely handle the optional polygon file ---
        string polygonFileName = "";
        string polygonFilePath = ""; // Start with an empty path
        var polygonFile = values.Files["UploadNoteFile"];

        if (polygonFile != null && polygonFile.Length > 0)
        {
            // Only process the polygon file if it exists
            polygonFileName = "Polygon_" + date.ToString("MMddyyyyHmmss") + Path.GetExtension(polygonFile.FileName);
            polygonFilePath = Path.Combine(directorypath, polygonFileName);
            using (var stream = new FileStream(polygonFilePath, FileMode.Create))
            {
                polygonFile.CopyTo(stream);
            }
        }

        // --- Save to Database ---
        tbl_upload_history excel_details = new tbl_upload_history
        {
            remarks = Remarks,
            file_name = mainFileName,
            polygon_file = polygonFileName,
            file_type = UploadFileType,
            status = 1, // Assume success initially
            uploaded_by = UserID,
            uploaded_on = DateTime.Now
        };
        db.tbl_upload_history.Add(excel_details);
        db.SaveChanges();

        // --- Process the files ---
        ProcessCSVController csv = new ProcessCSVController(db, cf);
        string errorMsg;
        
        // Pass the potentially empty polygonFilePath. The processor will handle it.
        bool success = csv.Process(excel_details.id, mainFilePath, mainFile.FileName, polygonFilePath, UploadFileType, 0, Remarks, out errorMsg);

        if (success)
        {
            message.Status = 1;
            message.Message = "File uploaded and processed successfully.";
        }
        else
        {
            message.Status = 0;
            message.Message = errorMsg; // Show the error from the processor
            excel_details.status = 0; // Update status to Failed
        }

        // Save final status and errors
        excel_details.errors = errorMsg;
        db.SaveChanges();
    }
    catch (Exception ex)
    {
        message.Status = 0;
        message.Message = "A critical error occurred: " + ex.Message;
        // Optionally log the full exception
    }
    return Json(message);
}

        // [HttpPost]
        // [RequestSizeLimit(100_000_000)]
        // public JsonResult UploadExcelFile([FromForm] IFormCollection values)
        // {
        //     ReturnAPIResponse message = new ReturnAPIResponse();
        //     try
        //     {
        //         // Safe form value access with proper null checking
        //         string Remarks = GetFormValue(values, "remarks");
        //         string token = GetFormValue(values, "token");
        //         string ip = GetFormValue(values, "ip");
        //         string ProjectName = GetFormValue(values, "ProjectName");
        //         string SessionIds = GetFormValue(values, "SessionIds");

        //         int UploadFileType = 0;
        //         string uploadFileTypeValue = GetFormValue(values, "UploadFileType");
        //         if (!string.IsNullOrEmpty(uploadFileTypeValue))
        //         {
        //             int.TryParse(uploadFileTypeValue, out UploadFileType);
        //         }

        //         cf.SessionCheck();
        //         message.Status = 1;

        //         if (message.Status == 1)
        //         {
        //             var remarksValidation = InputValidator.ValidateRemarks(Remarks, "Remarks");
        //             if (!remarksValidation.isValid)
        //             {
        //                 message.Status = 0;
        //                 message.Message = remarksValidation.errorMessage;
        //                 return Json(message);
        //             }
        //             Remarks = remarksValidation.sanitized;

        //             if (values.Files == null || values.Files.Count == 0)
        //             {
        //                 message.Status = 0;
        //                 message.Message = "Please select a file to upload.";
        //                 return Json(message);
        //             }

        //             int UserID = 0;
        //             if (HttpContext != null && HttpContext.Session != null)
        //             {
        //                 object userIdObj = HttpContext.Session.GetInt32("UserID");
        //                 if (userIdObj != null)
        //                 {
        //                     UserID = Convert.ToInt32(userIdObj);
        //                 }
        //             }

        //             string file_name = "";
        //             var FileUpload1 = values.Files["UploadFile"];
        //             var FileUpload2 = values.Files["UploadNoteFile"];

        //             if (FileUpload1 == null || FileUpload1.Length == 0)
        //             {
        //                 message.Status = 0;
        //                 message.Message = "Please select a valid main file to upload.";
        //                 return Json(message);
        //             }

        //             int MaxContent = (int)FileUpload1.Length;
        //             float SizeInKB = MaxContent / 1024;

        //             if (SizeInKB > 0)
        //             {
        //                 var directorypath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedExcels");
        //                 string contentType = FileUpload1.ContentType;

        //                 if (CommonFunction.ValidateCSVZipFile(contentType))
        //                 {
        //                     DateTime Date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, INDIAN_ZONE);

        //                     if (!Directory.Exists(directorypath))
        //                     {
        //                         Directory.CreateDirectory(directorypath);
        //                     }

        //                     string inboundPolygonFile = "";
        //                     if (FileUpload2 != null && FileUpload2.Length > 0)
        //                     {
        //                         string extension1 = Path.GetExtension(FileUpload2.FileName);
        //                         MaxContent = (int)FileUpload2.Length;
        //                         SizeInKB = MaxContent / 1024;

        //                         if (SizeInKB > 0 && (CommonFunction.ValidateCSVZipFile(FileUpload2.ContentType) || CommonFunction.ValidateGeoJsonFile(FileUpload2.ContentType)))
        //                         {
        //                             inboundPolygonFile = "Polygon_" + Date.ToString("MMddyyyyHmmss") + extension1;
        //                         }
        //                         else
        //                         {
        //                             message.Status = 0;
        //                             message.Message = "Please upload valid inbound polygon zip/csv file.";
        //                             return Json(message);
        //                         }
        //                     }

        //                     string extension = Path.GetExtension(FileUpload1.FileName);
        //                     file_name = "File_" + Date.ToString("MMddyyyyHmmss") + extension;

        //                     string path = Path.Combine(directorypath, file_name);
        //                     using (var stream = new FileStream(path, FileMode.Create))
        //                     {
        //                         FileUpload1.CopyTo(stream);
        //                     }

        //                     string polygonFilePath = "";
        //                     if (FileUpload2 != null && FileUpload2.Length > 0 && !string.IsNullOrEmpty(inboundPolygonFile))
        //                     {
        //                         polygonFilePath = Path.Combine(directorypath, inboundPolygonFile);
        //                         using (var stream = new FileStream(polygonFilePath, FileMode.Create))
        //                         {
        //                             FileUpload2.CopyTo(stream);
        //                         }
        //                     }

        //                     string sheet_msg = "";
        //                     bool HasSpChar = false;

        //                     if (!HasSpChar)
        //                     {
        //                         tbl_upload_history excel_details = new tbl_upload_history();
        //                         excel_details.remarks = Remarks;
        //                         excel_details.file_name = file_name;
        //                         excel_details.polygon_file = inboundPolygonFile;
        //                         excel_details.file_type = UploadFileType;
        //                         excel_details.status = 1;
        //                         excel_details.uploaded_by = UserID;
        //                         excel_details.uploaded_on = DateTime.Now;
        //                         db.tbl_upload_history.Add(excel_details);
        //                         db.SaveChanges();
        //                         message.Status = 1;
        //                         message.Message = "File has been uploaded successfully.";

        //                         int projectId = 0;
        //                         if (UploadFileType == 2)
        //                         {
        //                             tbl_project objProject = new tbl_project();
        //                             objProject.project_name = ProjectName;
        //                             objProject.ref_session_id = SessionIds;
        //                             objProject.created_by_user_id = UserID;
        //                             objProject.created_by_user_name = cf.UserName;
        //                             objProject.status = 1;
        //                             db.tbl_project.Add(objProject);
        //                             db.SaveChanges();
        //                             projectId = objProject.id;
        //                         }

        //                         ProcessCSVController csv = new ProcessCSVController(db, cf);
        //                         string errorMsg = "";
        //                         bool ret = csv.Process(excel_details.id, path, FileUpload1.FileName, polygonFilePath, UploadFileType, projectId, Remarks, out errorMsg);

        //                         if (!ret)
        //                         {
        //                             excel_details.status = 0;
        //                             if (projectId > 0)
        //                             {
        //                                 var objProject = db.tbl_project.Where(a => a.id == projectId).FirstOrDefault();
        //                                 if (objProject != null)
        //                                 {
        //                                     objProject.status = 0;
        //                                     db.Entry(objProject).State = EntityState.Modified;
        //                                 }
        //                             }
        //                             db.SaveChanges();

        //                             message.Status = 0;
        //                             message.Message = errorMsg;
        //                         }
        //                         else
        //                         {
        //                             message.Message += errorMsg;
        //                         }

        //                         excel_details.errors = errorMsg;
        //                         db.SaveChanges();
        //                     }
        //                     else
        //                     {
        //                         message.Status = 0;
        //                         message.Message = "Special Characters are not allowed.";
        //                         if (!string.IsNullOrEmpty(sheet_msg))
        //                             message.Message = sheet_msg;

        //                         try
        //                         {
        //                             System.IO.File.Delete(path);
        //                         }
        //                         catch (Exception ex)
        //                         {
        //                         }
        //                     }
        //                 }
        //                 else
        //                 {
        //                     message.Status = 0;
        //                     message.Message = "Please upload only csv file.";
        //                 }
        //             }
        //             else
        //             {
        //                 message.Status = 0;
        //                 message.Message = "File size should be greater than 0KB.";
        //             }
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         Writelog writelog = new Writelog(db);
        //         writelog.write_exception_log(0, "AdminExcelUploadController", "UploadExcelFile", DateTime.Now, ex);
        //         message.Status = 0;
        //         message.Message = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        //     }
        //     return Json(message);
        // }

        // Helper method for safe form value access
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