using Microsoft.AspNetCore.Mvc;
using ObjectCounterApp.Web.Services;

namespace ObjectCounterApp.Web.Controllers
{
    [ApiController]
    [Route("api/traffic")]
    public sealed class FootTrafficController : ControllerBase
    {
        private readonly IFootTrafficService _footTrafficService;

        public FootTrafficController(IFootTrafficService footTrafficService)
        {
            _footTrafficService = footTrafficService;
        }

        [HttpGet("{cameraId}/live")]
        public IActionResult GetLive(string cameraId)
        {
            return Ok(new { cameraId, occupancy = _footTrafficService.GetLiveOccupancy(cameraId) });
        }

        [HttpGet("{cameraId}/{date?}")]
        public IActionResult GetDay(string cameraId, string? date)
        {
            DateOnly day;
            if (string.IsNullOrEmpty(date))
            {
                day = DateOnly.FromDateTime(DateTime.Now);
            }
            else if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out day))
            {
                return BadRequest("date must be in yyyy-MM-dd format.");
            }

            return Ok(_footTrafficService.GetDaySummary(cameraId, day));
        }
    }
}
