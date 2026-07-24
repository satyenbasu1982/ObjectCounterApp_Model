using Microsoft.AspNetCore.Mvc;
using ObjectCounterApp.Core;
using ObjectCounterApp.Web.Models;
using ObjectCounterApp.Web.Services;

namespace ObjectCounterApp.Web.Controllers
{
    [ApiController]
    [Route("api/people")]
    public sealed class PeopleController : ControllerBase
    {
        private readonly IPeopleService _peopleService;

        public PeopleController(IPeopleService peopleService)
        {
            _peopleService = peopleService;
        }

        [HttpGet]
        public IActionResult GetNames() => Ok(new { names = _peopleService.ListNames() });

        [HttpGet("details")]
        public IActionResult GetDetails() => Ok(_peopleService.GetDetails());

        [HttpPost]
        public async Task<IActionResult> Enroll()
        {
            if (!Request.HasFormContentType)
            {
                return BadRequest("Expected multipart/form-data with a 'name' field and one or more 'photos' files.");
            }

            var form = await Request.ReadFormAsync();
            var name = form["name"].ToString().Trim();
            if (string.IsNullOrEmpty(name))
            {
                return BadRequest("A name is required.");
            }

            var photos = form.Files.GetFiles("photos");
            if (photos.Count == 0)
            {
                return BadRequest("At least one reference photo is required.");
            }

            var result = await _peopleService.EnrollAsync(name, photos);
            return Ok(result);
        }

        [HttpDelete("{name}")]
        public IActionResult Delete(string name)
        {
            return _peopleService.Remove(name) ? Ok() : NotFound();
        }

        [HttpPut("{name}")]
        public IActionResult Rename(string name, [FromBody] RenameRequestDto? body)
        {
            var newName = body?.NewName?.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                return BadRequest("A newName is required.");
            }

            return _peopleService.Rename(name, newName) switch
            {
                RenameResult.Success => Ok(),
                RenameResult.NameTaken => Conflict("That name is already in use."),
                _ => NotFound()
            };
        }
    }
}
