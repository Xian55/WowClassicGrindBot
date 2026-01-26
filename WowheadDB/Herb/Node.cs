using Newtonsoft.Json;

using SharedLib.Extensions;

using System.Collections.Generic;
using System.Numerics;

namespace WowheadDB;

public sealed class Node
{
    public List<List<float>> coords;
    public int level;
    public string name;
    public int type;
    public int id;

    [JsonIgnore]
    public Vector3[] MapCoords => VectorExt.FromList(coords);
}
