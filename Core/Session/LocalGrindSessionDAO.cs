using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using static Newtonsoft.Json.JsonConvert;

namespace Core.Session;

// this is gonna save the bot session data locally atm
// there will be an AWS session handler later to upload the session data to AWS S3
// the idea is we will have two session data handlers working at the same time
public sealed class LocalGrindSessionDAO : IGrindSessionDAO
{
    private readonly DataConfig dataConfig;
    private readonly int[] expList;

    public LocalGrindSessionDAO(DataConfig dataConfig)
    {
        this.dataConfig = dataConfig;

        expList = ExperienceProvider.Get(dataConfig);

        if (!Directory.Exists(dataConfig.ExpHistory))
            Directory.CreateDirectory(dataConfig.ExpHistory);
    }

    public async Task<IEnumerable<GrindSession>> LoadAsync()
    {
        var filePaths = Directory.EnumerateFiles(dataConfig.ExpHistory, "*.json");

        var sessions = (await Task.WhenAll(filePaths.Select(async fileName =>
        {
            var fileContent = await File.ReadAllTextAsync(fileName);
            var session = DeserializeObject<GrindSession>(fileContent);
            if (session != null)
            {
                session.ExpList = expList;
                session.PathName = Path.GetFileNameWithoutExtension(session.PathName) ?? string.Empty;
            }
            return session;
        })))
        .Where(s => s != null)
        .Cast<GrindSession>()
        .OrderByDescending(s => s.SessionStart);

        return !sessions.Any()
            ? []
            : sessions;
    }

    public void Save(GrindSession session)
    {
        string json = SerializeObject(session);
        File.WriteAllText(Path.Join(dataConfig.ExpHistory, $"{session.SessionId}.json"), json);
    }
}