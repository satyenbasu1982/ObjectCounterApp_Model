using Microsoft.AspNetCore.Mvc;
using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Models;
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

        [HttpPut("{date}/{name}/sessions/{index:int}")]
        public IActionResult UpdateSession(string date, string name, int index, [FromBody] SessionEditRequestDto? body)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day))
            {
                return BadRequest("date must be in yyyy-MM-dd format.");
            }
            if (body is null)
            {
                return BadRequest("start and end are required.");
            }

            return _attendanceService.UpdateSession(day, name, index, body.Start, body.End) switch
            {
                AttendanceEditResult.Success => Ok(),
                AttendanceEditResult.InvalidRange => BadRequest("end must be after start."),
                AttendanceEditResult.SessionIndexOutOfRange => NotFound(),
                _ => NotFound()
            };
        }

        [HttpPost("{date}/{name}/sessions")]
        public IActionResult AddSession(string date, string name, [FromBody] SessionEditRequestDto? body)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day))
            {
                return BadRequest("date must be in yyyy-MM-dd format.");
            }
            if (body is null)
            {
                return BadRequest("start and end are required.");
            }

            return _attendanceService.AddSession(day, name, body.Start, body.End) switch
            {
                AttendanceEditResult.Success => Ok(),
                AttendanceEditResult.InvalidRange => BadRequest("end must be after start."),
                _ => NotFound()
            };
        }

        [HttpDelete("{date}/{name}/sessions/{index:int}")]
        public IActionResult DeleteSession(string date, string name, int index)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day))
            {
                return BadRequest("date must be in yyyy-MM-dd format.");
            }

            return _attendanceService.DeleteSession(day, name, index) ? Ok() : NotFound();
        }

        [HttpGet("range")]
        public IActionResult GetRange([FromQuery] string start, [FromQuery] string end)
        {
            if (!TryParseRange(start, end, out var startDate, out var endDate, out var error))
            {
                return BadRequest(error);
            }

            return Ok(_attendanceService.GetRangeSummary(startDate, endDate));
        }

        [HttpGet("range/export")]
        public IActionResult ExportRange([FromQuery] string start, [FromQuery] string end)
        {
            if (!TryParseRange(start, end, out var startDate, out var endDate, out var error))
            {
                return BadRequest(error);
            }

            var csvBytes = _attendanceService.ExportRangeCsv(startDate, endDate);
            return File(csvBytes, "text/csv", $"attendance-{start}-to-{end}.csv");
        }

        private static bool TryParseRange(string start, string end, out DateOnly startDate, out DateOnly endDate, out string? error)
        {
            startDate = default;
            endDate = default;

            if (!DateOnly.TryParseExact(start, "yyyy-MM-dd", out startDate) || !DateOnly.TryParseExact(end, "yyyy-MM-dd", out endDate))
            {
                error = "start and end must be in yyyy-MM-dd format.";
                return false;
            }
            if (endDate < startDate)
            {
                error = "end must not be before start.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
