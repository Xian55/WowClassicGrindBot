using Microsoft.AspNetCore.Mvc;

using Newtonsoft.Json;

using PPather;
using PPather.Graph;

namespace Frontend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class DangerZoneController : ControllerBase
{
    private readonly DataConfig dataConfig;
    private readonly PPatherService service;

    public DangerZoneController(DataConfig dataConfig, PPatherService service)
    {
        this.dataConfig = dataConfig;
        this.service = service;
    }

    /// <summary>
    /// Re-reads the authored roads and danger zones. These are query-time costs,
    /// so this takes effect on the next path with no rebake.
    /// </summary>
    [HttpPost("reload")]
    public IActionResult Reload()
    {
        service.ReloadCostZones();
        return Ok();
    }

    [HttpGet]
    public IActionResult Get([FromQuery] string continent)
    {
        if (string.IsNullOrWhiteSpace(continent))
            return BadRequest("continent is required");

        string dangerDir = System.IO.Path.Join(dataConfig.Road, continent, "dangerzone");
        if (!Directory.Exists(dangerDir))
            return Ok(new Dictionary<int, DangerZoneData>());

        Dictionary<int, DangerZoneData> result = [];
        foreach (string file in Directory.GetFiles(dangerDir, "*.json"))
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(file);
            if (int.TryParse(fileName, out int uiMapId))
            {
                string json = System.IO.File.ReadAllText(file);
                DangerZoneData data = JsonConvert.DeserializeObject<DangerZoneData>(json) ?? new DangerZoneData();
                result[uiMapId] = data;
            }
        }

        return Ok(result);
    }

    [HttpPost]
    public IActionResult Post([FromQuery] string continent, [FromQuery] int uiMapId, [FromBody] DangerZoneData data)
    {
        if (string.IsNullOrWhiteSpace(continent))
            return BadRequest("continent is required");

        string dangerDir = System.IO.Path.Join(dataConfig.Road, continent, "dangerzone");

        if (data.Circles.Length == 0 && data.Rectangles.Length == 0)
        {
            // Cleanup: delete empty zone file
            string filePath = System.IO.Path.Join(dangerDir, uiMapId + ".json");
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);

            return Ok();
        }

        if (!Directory.Exists(dangerDir))
            Directory.CreateDirectory(dangerDir);

        string path = System.IO.Path.Join(dangerDir, uiMapId + ".json");
        string json = JsonConvert.SerializeObject(data, Formatting.Indented);
        System.IO.File.WriteAllText(path, json);

        return Ok();
    }
}
