using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SignalTracker.Helper;
using SignalTracker.Models;

namespace SignalTracker.Controllers
{
    public class SettingController : Controller
    {
        ApplicationDbContext db = null;
        CommonFunction cf = null;
        public SettingController(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            db = context;
            cf = new CommonFunction(context, httpContextAccessor);
        }
        public IActionResult SettingIndex()
        {
            if (!cf.SessionCheck())
                return RedirectToAction("Index", "Home");
            return View();
        }
        [HttpGet]
        public JsonResult GetThresholdSettings()
        {
            ReturnAPIResponse message = new ReturnAPIResponse();
            try
            {
                cf.SessionCheck();
                var setting = db.thresholds.FirstOrDefault(x => x.user_id == cf.UserId);

                if (setting == null)
                {
                    setting = db.thresholds.FirstOrDefault(x => x.is_default == 1);
                }
                message.Status = 1;
                message.Data= setting;
            }
            catch (Exception ex)
            {
                message.Message = DisplayMessage.ErrorMessage + " " + ex.Message;
            }
            return Json(message);
        }

        [HttpPost]
        public JsonResult SaveThreshold([FromBody] thresholds model)
        {
            ReturnAPIResponse response = new ReturnAPIResponse();

            try
            {
                if (model != null && cf.SessionCheck())
                {
                    var existing = db.thresholds.FirstOrDefault(x => x.user_id == cf.UserId);

                    if (existing != null)
                    {
                        // Update existing
                        existing.rsrp_json = model.rsrp_json;
                        existing.rsrq_json = model.rsrq_json;
                        existing.sinr_json = model.sinr_json;
                        existing.dl_thpt_json = model.dl_thpt_json;
                        existing.ul_thpt_json = model.ul_thpt_json;
                        existing.volte_call = model.volte_call;
                        existing.lte_bler_json = model.lte_bler_json;
                        existing.mos_json = model.mos_json;
                        //existing.is_default = model.is_default;
                        db.Entry(existing).State=Microsoft.EntityFrameworkCore.EntityState.Modified;

                    }
                    else
                    {
                        // Insert new
                        model.id = 0;
                        model.is_default = 0;
                        model.user_id = cf.UserId;
                        db.thresholds.Add(model);
                    }

                    db.SaveChanges();

                    response.Status = 1;
                    response.Message = "Threshold saved successfully.";
                    response.Data = model.id;
                }
                else
                    response.Message = "Invalid Request";
            }
            catch (Exception ex)
            {
                response.Status = 0;
                response.Message = "Error: " + ex.Message;
            }

            return Json(response);
        }
    }
}
