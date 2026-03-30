using CarCareTracker.External.Interfaces;
using CarCareTracker.Filter;
using CarCareTracker.Models;
using CarCareTracker.Models.LoggerSync;
using Microsoft.AspNetCore.Mvc;

namespace CarCareTracker.Controllers
{
    public partial class APIController
    {
        [TypeFilter(typeof(APIKeyFilter), Arguments = new object[] { HouseholdPermission.View })]
        [HttpPost]
        [Route("/api/tracker/sync/check")]
        public IActionResult TrackerSyncCheck([FromBody] TrackerSyncCheckRequest request)
        {
            if (request == null || request.VehicleId == default)
            {
                Response.StatusCode = 400;
                return Json(OperationResponse.Failed("Must provide a valid vehicleId"));
            }
            var received = _receivedFileDataAccess.GetReceivedFilenames(request.VehicleId);
            var needed = request.Files.Where(f => !received.Contains(f)).ToList();
            return Json(new { needed });
        }

        [TypeFilter(typeof(APIKeyFilter), Arguments = new object[] { HouseholdPermission.Edit })]
        [HttpPost]
        [Route("/api/tracker/sync/upload")]
        public async Task<IActionResult> TrackerSyncUpload(int vehicleId, string filename)
        {
            if (vehicleId == default || string.IsNullOrWhiteSpace(filename))
            {
                Response.StatusCode = 400;
                return Json(OperationResponse.Failed("Must provide vehicleId and filename"));
            }
            try
            {
                var directory = Path.Combine(_webEnv.ContentRootPath, "data", "telemetry", vehicleId.ToString());
                Directory.CreateDirectory(directory);
                var safeName = Path.GetFileName(filename);
                var filePath = Path.Combine(directory, safeName);
                using var fileStream = new FileStream(filePath, FileMode.Create);
                await Request.Body.CopyToAsync(fileStream);
                _receivedFileDataAccess.MarkFileReceived(vehicleId, filename);
                return Json(OperationResponse.Succeed("File Saved"));
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(OperationResponse.Failed(ex.Message));
            }
        }


        [TypeFilter(typeof(APIKeyFilter), Arguments = new object[] { HouseholdPermission.View })]
        [HttpGet]
        [Route("/api/tracker/firmware/latest")]
        public IActionResult GetLatestFirmware()
        {
            var firmware = _firmwareDataAccess.GetLatestActiveFirmware();
            if (firmware == null)
            {
                Response.StatusCode = 204;
                return Json(null);
            }
            return Json(new { version = firmware.Version, size = firmware.FileSize, md5 = firmware.Md5Hash });
        }
    }
}
