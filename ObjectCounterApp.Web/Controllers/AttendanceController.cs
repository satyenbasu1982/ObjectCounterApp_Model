using Microsoft.AspNetCore.Mvc;
using ObjectCounterApp.Web.Services;

namespace ObjectCounterApp.Web.Controllers
{
    [ApiController]
    [Route("api/attendance")]
    public sealed class AttendanceController : ControllerBase
    {
        private readonly IAttendanceService _attendanceService;

        public AttendanceController(IAttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
        }

        [HttpGet("{date?}")]
        public IActionResult GetDay(string? date)
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

            return Ok(_attendanceService.GetDaySummary(day));
        }
    }
}
