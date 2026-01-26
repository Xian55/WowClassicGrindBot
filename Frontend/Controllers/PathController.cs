using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using PPather;

using SharedLib.Converters;

using System.Numerics;
using System.Text.Json;

namespace Frontend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PathController : ControllerBase
{
    private readonly DataConfig dataConfig;
    private readonly PPatherService service;

    private readonly JsonSerializerOptions options = new()
    {
        Converters =
        {
            new Vector3Converter(true)
        }
    };

    public PathController(DataConfig dataConfig, PPatherService service)
    {
        this.dataConfig = dataConfig;
        this.service = service;
    }

    [HttpGet()]
    public IActionResult Get(string filter)
    {
        string[] pathFiles = Directory.GetFiles(dataConfig.Path, $"*{filter}*.json", SearchOption.AllDirectories);

        for (int i = 0; i < pathFiles.Length; i++)
        {
            string path = pathFiles[i];
            pathFiles[i] = path.Replace(dataConfig.Root, "");
        }

        return Ok(pathFiles);
    }

    [HttpPost("SavePath")]
    public IActionResult SavePath([FromQuery] string fileName, [FromBody] Vector3[] mapPoints)
    {
        string filePath = Path.Join(dataConfig.Path, fileName);
        System.IO.File.WriteAllText(filePath, JsonSerializer.Serialize(mapPoints, options));

        return Ok();
    }

    [HttpGet("GetAreaIdAndZ")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK, "application/json")]
    public IActionResult GetAreaIdAndZ(int mapid, float x, float y)
    {
        service.Initialise(mapid);

        (int areaId, float z) = service.GetAreaIdAndZ(new Vector3(x, y, 0));
        return new JsonResult(new { areaId, z }, options);
    }
}