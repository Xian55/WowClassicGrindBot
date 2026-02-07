// NPC Color Match Compute Shader
// Scans rows of a captured screen texture for NPC name colors (red, green, yellow, gray)
// and produces line segments (xStart, xEnd, y) for matching pixel runs.
//
// Thread strategy:
//   Dispatch: ceil(rowWidth / 256) groups per row, numRows groups vertically
//   numthreads(256, 1, 1) -- each thread checks one pixel
//   Thread 0 of each group scans shared memory to extract contiguous segments

// Input texture (B8G8R8A8_UNorm, automatically swizzled to RGBA by SRV)
Texture2D<float4> InputTexture : register(t0);

// Output structured buffer for line segments
struct GpuLineSegment
{
    int xStart;
    int xEnd;
    int y;
    int _pad;
};

RWStructuredBuffer<GpuLineSegment> OutputSegments : register(u0);

// Atomic counter for output segments
RWByteAddressBuffer Counter : register(u1);

// Constant buffer with scan parameters
cbuffer ScanParams : register(b0)
{
    int AreaLeft;
    int AreaTop;
    int AreaRight;
    int AreaBottom;
    float MinLength;
    float MinEndLength;

    // Up to 5 target colors with squared fuzz thresholds
    int NumColors;
    int _pad0;

    float4 TargetColor0;   // xyz = RGB [0,1], w = unused
    float FuzzSqr0;        // squared distance threshold in [0,1] space
    float3 _pad1;

    float4 TargetColor1;
    float FuzzSqr1;
    float3 _pad2;

    float4 TargetColor2;
    float FuzzSqr2;
    float3 _pad3;

    float4 TargetColor3;
    float FuzzSqr3;
    float3 _pad5;

    float4 TargetColor4;
    float FuzzSqr4;
    float3 _pad6;

    int MaxSegments;
    int3 _pad4;
};

#define GROUP_SIZE 256

groupshared uint g_match[GROUP_SIZE];

bool ColorMatch(float3 pixel)
{
    float3 d0 = pixel - TargetColor0.xyz;
    float dist0 = dot(d0, d0);
    if (dist0 <= FuzzSqr0)
        return true;

    if (NumColors > 1)
    {
        float3 d1 = pixel - TargetColor1.xyz;
        float dist1 = dot(d1, d1);
        if (dist1 <= FuzzSqr1)
            return true;
    }

    if (NumColors > 2)
    {
        float3 d2 = pixel - TargetColor2.xyz;
        float dist2 = dot(d2, d2);
        if (dist2 <= FuzzSqr2)
            return true;
    }

    if (NumColors > 3)
    {
        float3 d3 = pixel - TargetColor3.xyz;
        float dist3 = dot(d3, d3);
        if (dist3 <= FuzzSqr3)
            return true;
    }

    if (NumColors > 4)
    {
        float3 d4 = pixel - TargetColor4.xyz;
        float dist4 = dot(d4, d4);
        if (dist4 <= FuzzSqr4)
            return true;
    }

    return false;
}

[numthreads(GROUP_SIZE, 1, 1)]
void CSMain(
    uint3 groupId : SV_GroupID,
    uint groupIndex : SV_GroupIndex,
    uint3 dispatchId : SV_DispatchThreadID)
{
    // Calculate which row this group is scanning
    int areaWidth = AreaRight - AreaLeft;
    int groupsPerRow = (areaWidth + GROUP_SIZE - 1) / GROUP_SIZE;
    int row = groupId.x / groupsPerRow;
    int groupInRow = groupId.x % groupsPerRow;

    int y = AreaTop + row;
    int x = AreaLeft + groupInRow * GROUP_SIZE + (int)groupIndex;

    // Check if this pixel is in bounds and matches
    bool isMatch = false;
    if (x < AreaRight && y < AreaBottom)
    {
        // SRV swizzles B8G8R8A8_UNorm to float4(R, G, B, A)
        float4 pixel = InputTexture.Load(int3(x, y, 0));
        isMatch = ColorMatch(pixel.rgb);
    }

    g_match[groupIndex] = isMatch ? 1u : 0u;

    GroupMemoryBarrierWithGroupSync();

    // Thread 0 scans shared memory to extract line segments
    if (groupIndex != 0)
        return;

    int segXStart = -1;
    int segXEnd = -1;
    int scanEnd = min((int)(groupInRow * GROUP_SIZE + GROUP_SIZE), areaWidth);
    int scanStart = groupInRow * GROUP_SIZE;

    int localCount = 0;
    GpuLineSegment localSegs[8]; // max segments per group

    for (int lx = 0; lx < GROUP_SIZE && (scanStart + lx) < areaWidth; lx++)
    {
        int globalX = AreaLeft + scanStart + lx;

        if (g_match[lx] == 0u)
        {
            // Skip non-matching pixels (same as CPU path).
            // Text characters have small gaps; only the matching-pixel
            // gap check (MinLength) should terminate a segment.
            continue;
        }

        // Pixel matches
        if (segXStart >= 0 && (globalX - segXEnd) < (int)MinLength)
        {
            // Extend current segment (gap within tolerance)
        }
        else
        {
            // Start new segment (or emit old one)
            if (segXStart >= 0 && (segXEnd - segXStart) > MinEndLength)
            {
                if (localCount < 8)
                {
                    localSegs[localCount].xStart = segXStart;
                    localSegs[localCount].xEnd = segXEnd;
                    localSegs[localCount].y = y;
                    localSegs[localCount]._pad = 0;
                    localCount++;
                }
            }
            segXStart = globalX;
        }
        segXEnd = globalX;
    }

    // Emit final segment if in progress
    if (segXStart >= 0 && (segXEnd - segXStart) > MinEndLength)
    {
        if (localCount < 8)
        {
            localSegs[localCount].xStart = segXStart;
            localSegs[localCount].xEnd = segXEnd;
            localSegs[localCount].y = y;
            localSegs[localCount]._pad = 0;
            localCount++;
        }
    }

    if (localCount == 0)
        return;

    // Atomically reserve space in the output buffer
    uint baseIndex;
    Counter.InterlockedAdd(0, (uint)localCount, baseIndex);

    if ((int)baseIndex + localCount > MaxSegments)
        return;

    for (int s = 0; s < localCount; s++)
    {
        OutputSegments[baseIndex + s] = localSegs[s];
    }
}
