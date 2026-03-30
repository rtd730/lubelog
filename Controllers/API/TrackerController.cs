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
                var safeName = filename.Replace("..", "").TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var filePath = Path.Combine(directory, safeName);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
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

        [TypeFilter(typeof(APIKeyFilter), Arguments = new object[] { HouseholdPermission.Edit })]
        [HttpPost]
        [Route("/api/tracker/firmware/upload")]
        public async Task<IActionResult> UploadFirmware(string version, string notes = "")
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                Response.StatusCode = 400;
                return Json(OperationResponse.Failed("Must provide version"));
            }
            try
            {
                var directory = Path.Combine(_webEnv.ContentRootPath, "data", "firmware");
                Directory.CreateDirectory(directory);
                var fileName = $"firmware_{version}.bin";
                var filePath = Path.Combine(directory, fileName);
                using var md5 = System.Security.Cryptography.MD5.Create();
                using var fileStream = new FileStream(filePath, FileMode.Create);
                await Request.Body.CopyToAsync(fileStream);
                fileStream.Position = 0;
                var hash = BitConverter.ToString(md5.ComputeHash(fileStream)).Replace("-", "").ToLower();
                var record = new FirmwareRecord
                {
                    Version = version,
                    FilePath = fileName,
                    Md5Hash = hash,
                    FileSize = fileStream.Length,
                    Notes = notes,
                    IsActive = true
                };
                _firmwareDataAccess.SaveFirmware(record);
                return Json(OperationResponse.Succeed("Firmware uploaded"));
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(OperationResponse.Failed(ex.Message));
            }
        }

        [TypeFilter(typeof(APIKeyFilter), Arguments = new object[] { HouseholdPermission.View })]
        [HttpGet]
        [Route("/api/tracker/firmware/download")]
        public IActionResult DownloadFirmware()
        {
            var firmware = _firmwareDataAccess.GetLatestActiveFirmware();
            if (firmware == null)
            {
                Response.StatusCode = 204;
                return Json(null);
            }
            var filePath = Path.Combine(_webEnv.ContentRootPath, "data", "firmware", firmware.FilePath);
            if (!System.IO.File.Exists(filePath))
            {
                Response.StatusCode = 404;
                return Json(OperationResponse.Failed("Firmware file not found"));
            }
            return PhysicalFile(filePath, "application/octet-stream", firmware.FilePath);
        }

    }
}
