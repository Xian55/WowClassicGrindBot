using System;
using System.Buffers;
using System.Runtime.CompilerServices;

using Game;

using Microsoft.Extensions.Logging;

using SharedLib.NpcFinder;

using SixLabors.ImageSharp;

using Vortice.Direct3D11;

using static SharedLib.NpcFinder.NpcNameColors;

namespace Core;

/// <summary>
/// GPU-accelerated line segment provider using a DirectX 11 compute shader.
/// Falls back to CPU provider on initialization or dispatch failure.
/// </summary>
public sealed class GpuLineSegmentProvider : IConfigurableLineSegmentProvider, IDisposable
{
    private const int MAX_SEGMENTS = 4096;
    private const int MAX_COLORS = 5;
    private const int RESOLUTION = 16;

    private readonly ILogger logger;
    private readonly IGpuTextureProvider gpuProvider;
    private readonly CpuLineSegmentProvider cpuFallback;
    private readonly NpcGpuResources? gpuResources;

    private const int FALLBACK_COOLDOWN = 100;
    private bool permanentFallback;
    private int fallbackFramesLeft;

    private SearchMode searchMode;
    private NpcNames nameType;

    private LineSegment[]? segments;

    public GpuLineSegmentProvider(
        ILogger logger,
        IGpuTextureProvider gpuProvider,
        CpuLineSegmentProvider cpuFallback)
    {
        this.logger = logger;
        this.gpuProvider = gpuProvider;
        this.cpuFallback = cpuFallback;

        try
        {
            gpuResources = new NpcGpuResources(logger,
                gpuProvider.Device, gpuProvider.DeviceContext);

            if (!gpuResources.Initialize())
            {
                permanentFallback = true;
                logger.LogWarning("[GpuLineSegmentProvider] Initialization failed, using CPU fallback");
            }
            else
            {
                logger.LogInformation("[GpuLineSegmentProvider] GPU compute shader initialized");
            }
        }
        catch (Exception ex)
        {
            permanentFallback = true;
            logger.LogWarning(ex, "[GpuLineSegmentProvider] Failed to create GPU resources, using CPU fallback");
        }
    }

    public void Configure(SearchMode searchMode, NpcNames nameType)
    {
        this.searchMode = searchMode;
        this.nameType = nameType;
        cpuFallback.Configure(searchMode, nameType);
    }

    [SkipLocalsInit]
    public ReadOnlySpan<LineSegment> GetLineSegments(
        Rectangle area, float minLength, float minEndLength)
    {
        if (permanentFallback || gpuResources == null || !gpuResources.IsInitialized)
        {
            return cpuFallback.GetLineSegments(area, minLength, minEndLength);
        }

        if (fallbackFramesLeft > 0)
        {
            fallbackFramesLeft--;
            return cpuFallback.GetLineSegments(area, minLength, minEndLength);
        }

        ID3D11Texture2D? capturedTexture = gpuProvider.GetCapturedTexture();
        if (capturedTexture == null)
        {
            return cpuFallback.GetLineSegments(area, minLength, minEndLength);
        }

        try
        {
            return ExecuteGpu(capturedTexture, area, minLength, minEndLength);
        }
        catch (Exception ex) when (IsDeviceRemoved())
        {
            permanentFallback = true;
            logger.LogError(ex, "[GpuLineSegmentProvider] GPU device removed — permanent CPU fallback");
            return cpuFallback.GetLineSegments(area, minLength, minEndLength);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[GpuLineSegmentProvider] GPU dispatch failed, falling back to CPU for {Frames} frames", FALLBACK_COOLDOWN);
            fallbackFramesLeft = FALLBACK_COOLDOWN;
            return cpuFallback.GetLineSegments(area, minLength, minEndLength);
        }
    }

    [SkipLocalsInit]
    private ReadOnlySpan<LineSegment> ExecuteGpu(
        ID3D11Texture2D capturedTexture,
        Rectangle area, float minLength, float minEndLength)
    {
        // 1. Copy captured texture to SRV-capable texture
        gpuResources!.CopySourceTexture(capturedTexture);

        // 2. Build constant buffer
        ScanParamsCB cb = BuildScanParams(area, minLength, minEndLength);
        gpuResources.UpdateConstants(cb);

        // 3. Reset counter
        gpuResources.ResetCounter();

        // 4. Dispatch -- scan every row in the area (matches CPU ParallelRowIterator behavior)
        int areaWidth = area.Right - area.Left;
        int numRows = area.Bottom - area.Top;
        gpuResources.Dispatch(areaWidth, numRows);

        // 5. Read results
        var pooler = ArrayPool<LineSegment>.Shared;
        segments = pooler.Rent(MAX_SEGMENTS);

        int count = gpuResources.ReadResults(segments);

        if (count > 1)
        {
            // 6. Sort by (Y, XStart) — GPU output order is nondeterministic
            //    and downstream grouping in DetermineNpcs assumes Y-ascending order.
            Span<LineSegment> span = new(segments, 0, count);
            span.Sort(static (a, b) =>
            {
                int cmp = a.Y.CompareTo(b.Y);
                return cmp != 0 ? cmp : a.XStart.CompareTo(b.XStart);
            });

            // 7. Merge adjacent segments on the same row split at GROUP_SIZE
            //    boundaries. Matches CPU gap-bridging logic (LineSegmentOperation:72).
            int merged = 0;
            for (int i = 1; i < count; i++)
            {
                ref readonly LineSegment prev = ref span[merged];
                ref readonly LineSegment curr = ref span[i];

                if (curr.Y == prev.Y && (curr.XStart - prev.XEnd) < (int)minLength)
                {
                    span[merged] = new LineSegment(prev.XStart, curr.XEnd, prev.Y);
                }
                else
                {
                    span[++merged] = curr;
                }
            }
            count = merged + 1;
        }

        pooler.Return(segments);
        return new ReadOnlySpan<LineSegment>(segments, 0, count);
    }

    private ScanParamsCB BuildScanParams(
        Rectangle area, float minLength, float minEndLength)
    {
        ScanParamsCB cb = new()
        {
            AreaLeft = area.Left,
            AreaTop = area.Top,
            AreaRight = area.Right,
            AreaBottom = area.Bottom,
            MinLength = minLength,
            MinEndLength = minEndLength,
            MaxSegments = MAX_SEGMENTS
        };

        // Map NPC name type + search mode to target colors
        GetTargetColors(searchMode, nameType, ref cb);

        return cb;
    }

    private static void GetTargetColors(SearchMode mode, NpcNames names, ref ScanParamsCB cb)
    {
        // Convert byte colors to [0,1] float space for the shader
        const float inv = 1f / 255f;

        bool hasEnemy = (names & NpcNames.Enemy) != 0;
        bool hasFriendly = (names & NpcNames.Friendly) != 0;
        bool hasNeutral = (names & NpcNames.Neutral) != 0;
        bool hasCorpse = (names & NpcNames.Corpse) != 0;
        bool hasNamePlate = (names & NpcNames.NamePlate) != 0;

        int colorIdx = 0;
        float fuzzSqr;

        if (mode == SearchMode.Fuzzy)
        {
            const int fuzz = 40;
            // Fuzz in normalized space: (fuzz/255)^2
            fuzzSqr = (fuzz * inv) * (fuzz * inv);
            float fuzzCorpseSqr = (fuzzCorpse * inv) * (fuzzCorpse * inv);

            if (hasEnemy && colorIdx < MAX_COLORS)
            {
                SetColor(ref cb, colorIdx++, fE_R * inv, fE_G * inv, fE_B * inv, fuzzSqr);
            }
            if (hasNeutral && colorIdx < MAX_COLORS)
            {
                SetColor(ref cb, colorIdx++, fN_R * inv, fN_G * inv, fN_B * inv, fuzzSqr);
            }
            if (hasFriendly && colorIdx < MAX_COLORS)
            {
                SetColor(ref cb, colorIdx++, fF_R * inv, fF_G * inv, fF_B * inv, fuzzSqr);
            }
            if (hasCorpse && colorIdx < MAX_COLORS)
            {
                SetColor(ref cb, colorIdx++, fC_RGB * inv, fC_RGB * inv, fC_RGB * inv, fuzzCorpseSqr);
            }
            if (hasNamePlate)
            {
                // NamePlate_Normal: white (254,254,254)
                if (colorIdx < MAX_COLORS)
                {
                    SetColor(ref cb, colorIdx++, sNamePlate_N * inv, sNamePlate_N * inv, sNamePlate_N * inv, fuzzCorpseSqr);
                }
                // NamePlate_Hostile: yellow (254,254,0)
                if (colorIdx < MAX_COLORS)
                {
                    SetColor(ref cb, colorIdx++, sNamePlate_H_R * inv, sNamePlate_H_G * inv, sNamePlate_H_B * inv, fuzzCorpseSqr);
                }
            }
        }
        else
        {
            // Simple mode: tight fuzz
            fuzzSqr = (5f * inv) * (5f * inv);
            float fuzzCorpseSqr = (fuzzCorpse * inv) * (fuzzCorpse * inv);

            if (hasEnemy && colorIdx < MAX_COLORS)
            {
                SetColor(ref cb, colorIdx++, sE_R * inv, sE_G * inv, sE_B * inv, fuzzSqr);
            }
            if (hasNeutral && colorIdx < MAX_COLORS)
            {
                SetColor(ref cb, colorIdx++, sN_R * inv, sN_G * inv, sN_B * inv, fuzzSqr);
            }
            if (hasFriendly && colorIdx < MAX_COLORS)
            {
                SetColor(ref cb, colorIdx++, sF_R * inv, sF_G * inv, sF_B * inv, fuzzSqr);
            }
            if (hasCorpse && colorIdx < MAX_COLORS)
            {
                SetColor(ref cb, colorIdx++, fC_RGB * inv, fC_RGB * inv, fC_RGB * inv, fuzzCorpseSqr);
            }
            if (hasNamePlate)
            {
                // NamePlate_Normal: white (254,254,254)
                if (colorIdx < MAX_COLORS)
                {
                    SetColor(ref cb, colorIdx++, sNamePlate_N * inv, sNamePlate_N * inv, sNamePlate_N * inv, fuzzSqr);
                }
                // NamePlate_Hostile: yellow (254,254,0)
                if (colorIdx < MAX_COLORS)
                {
                    SetColor(ref cb, colorIdx++, sNamePlate_H_R * inv, sNamePlate_H_G * inv, sNamePlate_H_B * inv, fuzzSqr);
                }
            }
        }

        if (colorIdx == 0)
        {
            // No colors -- set impossible match
            SetColor(ref cb, 0, -1f, -1f, -1f, 0f);
            colorIdx = 1;
        }

        cb.NumColors = colorIdx;
    }

    private static void SetColor(ref ScanParamsCB cb, int index,
        float r, float g, float b, float fuzzSqr)
    {
        switch (index)
        {
            case 0:
                cb.TargetColor0 = new Vector4F(r, g, b, 0);
                cb.FuzzSqr0 = fuzzSqr;
                break;
            case 1:
                cb.TargetColor1 = new Vector4F(r, g, b, 0);
                cb.FuzzSqr1 = fuzzSqr;
                break;
            case 2:
                cb.TargetColor2 = new Vector4F(r, g, b, 0);
                cb.FuzzSqr2 = fuzzSqr;
                break;
            case 3:
                cb.TargetColor3 = new Vector4F(r, g, b, 0);
                cb.FuzzSqr3 = fuzzSqr;
                break;
            case 4:
                cb.TargetColor4 = new Vector4F(r, g, b, 0);
                cb.FuzzSqr4 = fuzzSqr;
                break;
        }
    }

    private bool IsDeviceRemoved()
    {
        try { return !gpuProvider.Device.DeviceRemovedReason.Success; }
        catch { return true; }
    }

    public void Dispose()
    {
        gpuResources?.Dispose();
    }
}
