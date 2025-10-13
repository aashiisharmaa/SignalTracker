
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

            //string filePath = "C:\\Users\\mahkom\\Downloads\\buildings.geojson";
            //ProcessCSVController csv = new ProcessCSVController(db, cf);

            //int rowInserted = 0;
            //int rowUpdated = 0;
            //List<string> errorList = new List<string>();
            //bool ret = csv.ProcessPredictionPloygonJson(filePath, 0, 0, ref rowInserted, ref rowUpdated, out errorList);

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
        public IActionResult GetUploadedExcelFiles(int FileType)
        {
            if (!cf.SessionCheck())
                return Unauthorized(new { Status = 0, Message = "Unauthorized" });

            var data = (from ob_excel in db.tbl_upload_history
                        join ob_user in db.tbl_user on ob_excel.uploaded_by equals ob_user.id
                        where ob_excel.file_type == FileType && ob_excel.uploaded_by == cf.UserId
                        orderby ob_excel.id descending
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
                        })
                        .Take(20)
                        .ToList();

            return Ok(new { Status = 1, Data = data });
        }
        [HttpPost]
        [RequestSizeLimit(100_000_000)]
        public JsonResult UploadExcelFile([FromForm] IFormCollection values)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();
            try
            {
                string Remarks = values["remarks"].ToString();
                string token = values["token"].ToString();
                string ip = values["ip"].ToString();
                string ProjectName = values["ProjectName"].ToString();
                string SessionIds = values["SessionIds"].ToString();

                int UploadFileType = Convert.ToInt32(values["UploadFileType"]);

                cf.SessionCheck();

                //message = cf.MatchToken(token);
                message.Status = 1;
                if (message.Status == 1)
                {
                    var remarksValidation = InputValidator.ValidateRemarks(values["remarks"].ToString(), "Remarks");
                    if (!remarksValidation.isValid)
                    {
                        message.Status = 0;
                        message.Message = remarksValidation.errorMessage;
                        return Json(message);
                    }
                    Remarks = remarksValidation.sanitized;

                    if (values.Files.Count > 0)
                    {
                        int UserID = 0;
                        if (HttpContext != null) UserID = Convert.ToInt16(HttpContext.Session.GetInt32("UserID"));
                        string file_name = "";
                        var FileUpload1 = values.Files["UploadFile"];
                        var FileUpload2 = values.Files["UploadNoteFile"];
                        if (FileUpload1 != null)
                        {
                            int MaxContent = (int)FileUpload1.Length;
                            float SizeInKB = MaxContent / 1024;
                            if (SizeInKB > 0)
                            {
                                var directorypath = Path.Combine(Directory.GetCurrentDirectory(), "UploadedExcels");
                                string contentType = FileUpload1.ContentType;
                                if (CommonFunction.ValidateCSVZipFile(contentType))
                                {
                                    DateTime Date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, INDIAN_ZONE);
                                    if (!Directory.Exists(directorypath))
                                    {
                                        Directory.CreateDirectory(directorypath);
                                    }
                                    string inboundPolygonFile = "";
                                    if (FileUpload2 != null)
                                    {
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


                                    string extension = Path.GetExtension(FileUpload1.FileName);
                                    file_name = "File_" + Date.ToString("MMddyyyyHmmss") + extension;

                                    string path = Path.Combine(directorypath, file_name);
                                    using (var stream = new FileStream(path, FileMode.Create))
                                    {
                                        FileUpload1.CopyTo(stream);
                                    }
                                    string polygonFilePath = "";

                                    if (FileUpload2 != null && !string.IsNullOrEmpty(inboundPolygonFile))
                                    {
                                        polygonFilePath = Path.Combine(directorypath, inboundPolygonFile);
                                        using (var stream = new FileStream(polygonFilePath, FileMode.Create))
                                        {
                                            FileUpload2.CopyTo(stream);
                                        }
                                    }

                                    string sheet_msg = "";
                                    bool HasSpChar = false;
                                    if (!HasSpChar)
                                    {
                                        tbl_upload_history excel_details = new tbl_upload_history();
                                        excel_details.remarks = Remarks;
                                        excel_details.file_name = file_name;
                                        excel_details.polygon_file = inboundPolygonFile;
                                        excel_details.file_type = UploadFileType;
                                        excel_details.status = 1;
                                        excel_details.uploaded_by = UserID;
                                        excel_details.uploaded_on = DateTime.Now;
                                        db.tbl_upload_history.Add(excel_details);
                                        db.SaveChanges();
                                        message.Status = 1;
                                        message.Message = "File has been uploaded successfully.";

                                        int projectId = 0;
                                        if (UploadFileType == 2)
                                        {
                                            tbl_project objProject = new tbl_project();
                                            objProject.project_name = ProjectName;
                                            objProject.ref_session_id = SessionIds;
                                            objProject.created_by_user_id = UserID;
                                            objProject.created_by_user_name = cf.UserName;
                                            objProject.status = 1;
                                            db.tbl_project.Add(objProject);
                                            db.SaveChanges();
                                            projectId = objProject.id;
                                        }

                                        ProcessCSVController csv = new ProcessCSVController(db, cf);
                                        string errorMsg = "";
                                        bool ret = csv.Process(excel_details.id, path, FileUpload1.FileName, polygonFilePath, UploadFileType, projectId, Remarks, out errorMsg);
                                        if (!ret)
                                        {
                                            excel_details.status = 0;
                                            // db.tbl_upload_history.Add(excel_details);
                                            if (projectId > 0)
                                            {
                                                var objProject = db.tbl_project.Where(a => a.id == projectId).FirstOrDefault();
                                                if (objProject != null)
                                                {
                                                    objProject.status = 0;
                                                    db.Entry(objProject).State = EntityState.Modified;
                                                }
                                            }
                                            db.SaveChanges();

                                            message.Status = 0;
                                            message.Message = errorMsg;
                                        }
                                        else
                                        {
                                            //message.Message += errorMsg;
                                            //message.token = cf.CreateToken(ip);
                                        }

                                        excel_details.errors = errorMsg;
                                        db.SaveChanges();
                                    }
                                    else
                                    {
                                        message.Status = 0;
                                        message.Message = "Special Characters are not allowed.";
                                        if (!string.IsNullOrEmpty(sheet_msg))
                                            message.Message = sheet_msg;
                                        try
                                        {
                                            System.IO.File.Delete(path);
                                        }
                                        catch (Exception ex)
                                        {
                                        }
                                    }

                                }
                                else
                                {
                                    message.Status = 0;
                                    message.Message = "Please upload only csv file.";
                                }
                            }
                            else
                            {
                                message.Status = 0;
                                message.Message = "File size should be greater than 0KB.";
                            }
                        }

                    }
                    else
                    {
                        message.Status = 0;
                        message.Message = "Please select excel file.";
                    }
                }
            }
            catch (Exception ex)
            {
                Writelog writelog = new Writelog(db);
                writelog.write_exception_log(0, "AdminExcelUploadController", "UploadExcelFile", DateTime.Now, ex);
                message.Status = 0;
                message.Message = ex.InnerException != null ? ex.InnerException.Message : ex.Message;

            }
            return Json(message);
        }
        [HttpGet]
        public JsonResult GetSessions(DateTime fromDate, DateTime toDate)
        {
            ReturnAPIResponse message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                //if (message.Status == 1)
                {
                    message.Status = 1;


                    var rawSessions = db.tbl_session.Where(s => s.start_time >= fromDate && s.end_time <= toDate).Join(db.tbl_user,
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



                    message.Data = formattedSessions; // naresh 13 JUl 2025 12:00 AM notes start address
                }
            }
            catch (Exception ex)
            {
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }
    }
}
