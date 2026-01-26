using Newtonsoft.Json;

using SharedLib.Extensions;

using System.Collections.Generic;
using System.Numerics;

namespace WowheadDB;

public sealed class NPC
{
    public List<List<float>> coords;

    public int level;
    public string name;
    public int type;
    public int id;
    public int reacthorde;
    public int reactalliance;
    public string description;

    [JsonIgnore]
    public Vector3[] MapCoords => VectorExt.FromList(coords);
}