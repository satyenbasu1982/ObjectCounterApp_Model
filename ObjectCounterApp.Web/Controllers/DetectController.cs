using Microsoft.AspNetCore.Mvc;
using ObjectCounterApp.Web.Services;

namespace ObjectCounterApp.Web.Controllers
{
    [ApiController]
    [Route("api/detect")]
    public sealed class DetectController : ControllerBase
    {
        private readonly IDetectionService _detectionService;

        public DetectController(IDetectionService detectionService)
        {
            _detectionService = detectionService;
        }

        [HttpPost]
        public async Task<IActionResult> Detect()
        {
            if (!Request.HasFormContentType)
            {
                return BadRequest("Expected multipart/form-data with a file field.");
            }

            var form = await Request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            bool identify = Request.Query["identify"] == "true";
            bool recordAttendance = identify && Request.Query["attendance"] == "true";

            var result = await _detectionService.DetectAsync(file, identify, recordAttendance);
            return Ok(result);
        }
    }
}
