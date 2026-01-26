using PPather.Data;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Core;

public interface IPathVizualizer : IDisposable
{
    HttpClient Client { get; }
    JsonSerializerOptions Options { get; }

    ValueTask DrawLines(List<LineArgs> lineArgs);
    ValueTask DrawSphere(SphereArgs args);
}