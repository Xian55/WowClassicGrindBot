using SharedLib;

using System.Numerics;

namespace Core.Database;

public readonly record struct NpcSearchResult(Creature Creature, Vector3 WorldPosition, float Distance);