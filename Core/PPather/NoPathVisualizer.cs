using PPather.Data;

using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Core;

internal sealed class NoPathVisualizer : IPathVizualizer
{
    public HttpClient Client => throw new System.NotImplementedException();

    public JsonSerializerOptions Options => throw new System.NotImplementedException();

    public void Dispose() { }

    public ValueTask DrawLines(List<LineArgs> lineArgs) => ValueTask.CompletedTask;

    public ValueTask DrawSphere(SphereArgs args) => ValueTask.CompletedTask;
}
