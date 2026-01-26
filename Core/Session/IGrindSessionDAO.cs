using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Session;

public interface IGrindSessionDAO
{
    Task<IEnumerable<GrindSession>> LoadAsync();

    void Save(GrindSession session);
}