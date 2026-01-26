
using System.Numerics;

namespace PPather;

public readonly record struct DrawMapPathRequest(int uiMapId, Vector3[] path);

public readonly record struct DrawWorldPathRequest(int mapId, Vector3[] path);