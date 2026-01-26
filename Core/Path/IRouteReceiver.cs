using System.Numerics;

namespace Core;

public interface IEditedRouteReceiver
{
    void ReceivePath(Vector3[] oldMap, Vector3[] newMap);
}
