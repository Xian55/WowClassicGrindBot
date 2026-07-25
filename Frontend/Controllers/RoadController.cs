using Microsoft.AspNetCore.Mvc;

using Newtonsoft.Json;

using PPather;
using PPather.Graph;

namespace Frontend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class RoadController : ControllerBase
{
    private readonly DataConfig dataConfig;
    private readonly PPatherService service;

    public RoadController(DataConfig dataConfig, PPatherService service)
    {
        this.dataConfig = dataConfig;
        this.service = service;
    }

    [HttpGet]
    public IActionResult Get([FromQuery] string continent)
    {
        if (string.IsNullOrWhiteSpace(continent))
            return BadRequest("continent is required");

        string continentDir = System.IO.Path.Join(dataConfig.Road, continent);
        if (Directory.Exists(continentDir))
        {
            Dictionary<int, RoadData> result = [];
            foreach (string file in Directory.GetFiles(continentDir, "*.json"))
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(file);
                if (int.TryParse(fileName, out int uiMapId))
                {
                    string json = System.IO.File.ReadAllText(file);
                    RoadData data = JsonConvert.DeserializeObject<RoadData>(json) ?? new RoadData();
                    result[uiMapId] = data;
                }
            }
            return Ok(result);
        }

        // Fallback: legacy single-file format
        string legacyPath = System.IO.Path.Join(dataConfig.Road, continent + ".json");
        if (System.IO.File.Exists(legacyPath))
        {
            string json = System.IO.File.ReadAllText(legacyPath);
            RoadData data = JsonConvert.DeserializeObject<RoadData>(json) ?? new RoadData();
            Dictionary<int, RoadData> result = new() { [0] = data };
            return Ok(result);
        }

        return Ok(new Dictionary<int, RoadData>());
    }

    [HttpPost]
    public IActionResult Post([FromQuery] string continent, [FromQuery] int uiMapId, [FromBody] RoadData data)
    {
        if (string.IsNullOrWhiteSpace(continent))
            return BadRequest("continent is required");

        string continentDir = System.IO.Path.Join(dataConfig.Road, continent);

        if (data.Roads.Length == 0)
        {
            // Cleanup: delete empty zone file
            string filePath = System.IO.Path.Join(continentDir, uiMapId + ".json");
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);

            return Ok();
        }

        if (!Directory.Exists(continentDir))
            Directory.CreateDirectory(continentDir);

        string path = System.IO.Path.Join(continentDir, uiMapId + ".json");
        string json = JsonConvert.SerializeObject(data, Formatting.Indented);
        System.IO.File.WriteAllText(path, json);

        return Ok();
    }

    [HttpPost("reload")]
    public IActionResult Reload()
    {
        service.ReloadCostZones();
        return Ok();
    }
}
