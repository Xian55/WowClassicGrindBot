using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using SharedLib.NpcFinder;

using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Core;

/// <summary>
/// GPU-side line segment for the compute shader output.
/// Must match the HLSL GpuLineSegment struct layout.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuLineSegment
{
    public int XStart;
    public int XEnd;
    public int Y;
    public int Pad;
}

/// <summary>
/// Constant buffer layout for the compute shader.
/// Must match the HLSL ScanParams cbuffer layout exactly.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ScanParamsCB
{
    public int AreaLeft;
    public int AreaTop;
    public int AreaRight;
    public int AreaBottom;
    public float MinLength;
    public float MinEndLength;
    public int NumColors;
    public int Pad0;

    public Vector4F TargetColor0;
    public float FuzzSqr0;
    public Vector3F Pad1;

    public Vector4F TargetColor1;
    public float FuzzSqr1;
    public Vector3F Pad2;

    public Vector4F TargetColor2;
    public float FuzzSqr2;
    public Vector3F Pad3;

    public Vector4F TargetColor3;
    public float FuzzSqr3;
    public Vector3F Pad5;

    public Vector4F TargetColor4;
    public float FuzzSqr4;
    public Vector3F Pad6;

    public int MaxSegments;
    public int Pad4X;
    public int Pad4Y;
    public int Pad4Z;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Vector4F
{
    public float X, Y, Z, W;

    public Vector4F(float x, float y, float z, float w)
    {
        X = x; Y = y; Z = z; W = w;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct Vector3F
{
    public float X, Y, Z;
}

/// <summary>
/// Manages all GPU resources needed for NPC color matching compute shader.
/// Created once and reused per frame.
/// </summary>
internal sealed class NpcGpuResources : IDisposable
{
    private const int MAX_SEGMENTS = 4096;
    private const int GROUP_SIZE = 256;
    private const string SHADER_RESOURCE_NAME = "Core.WoWScreen.Shaders.NpcColorMatch.hlsl";

    private readonly ILogger logger;
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;

    // Shader
    private ID3D11ComputeShader? computeShader;

    // CS-input texture (ShaderResource bind, Default usage)
    private ID3D11Texture2D? srvTexture;
    private ID3D11ShaderResourceView? srvView;
    private int srvWidth;
    private int srvHeight;

    // Output structured buffer
    private ID3D11Buffer? outputBuffer;
    private ID3D11UnorderedAccessView? outputUav;

    // Counter buffer (RWByteAddressBuffer, 4 bytes)
    private ID3D11Buffer? counterBuffer;
    private ID3D11UnorderedAccessView? counterUav;

    // Staging readback buffer
    private ID3D11Buffer? readbackBuffer;
    private ID3D11Buffer? counterReadbackBuffer;

    // Constant buffer
    private ID3D11Buffer? constantBuffer;

    public bool IsInitialized { get; private set; }

    public NpcGpuResources(ILogger logger, ID3D11Device device, ID3D11DeviceContext context)
    {
        this.logger = logger;
        this.device = device;
        this.context = context;
    }

    public bool Initialize()
    {
        try
        {
            CompileShader();
            CreateOutputBuffers();
            CreateConstantBuffer();
            IsInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to initialize GPU NPC resources -- falling back to CPU");
            IsInitialized = false;
            return false;
        }
    }

    private void CompileShader()
    {
        Assembly assembly = typeof(NpcGpuResources).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(SHADER_RESOURCE_NAME);

        if (stream == null)
            throw new InvalidOperationException(
                $"Embedded shader resource '{SHADER_RESOURCE_NAME}' not found");

        using MemoryStream ms = new();
        stream.CopyTo(ms);
        byte[] source = ms.ToArray();

        Compiler.Compile(source, "CSMain", SHADER_RESOURCE_NAME,
            "cs_5_0", out Blob shaderBlob, out Blob? errorBlob);

        if (shaderBlob.BufferPointer == IntPtr.Zero)
        {
            string errors = errorBlob != null
                ? Marshal.PtrToStringAnsi(errorBlob.BufferPointer) ?? "Unknown error"
                : "Unknown error";

            errorBlob?.Dispose();
            throw new InvalidOperationException($"Shader compilation failed: {errors}");
        }

        errorBlob?.Dispose();

        computeShader = device.CreateComputeShader(shaderBlob.AsSpan());
        shaderBlob.Dispose();

        logger.LogDebug("NPC compute shader compiled successfully");
    }

    private void CreateOutputBuffers()
    {
        // Output structured buffer for segments
        BufferDescription outputDesc = new()
        {
            ByteWidth = (uint)(MAX_SEGMENTS * Unsafe.SizeOf<GpuLineSegment>()),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.UnorderedAccess,
            MiscFlags = ResourceOptionFlags.BufferStructured,
            StructureByteStride = (uint)Unsafe.SizeOf<GpuLineSegment>()
        };
        outputBuffer = device.CreateBuffer(outputDesc);

        UnorderedAccessViewDescription outputUavDesc = new()
        {
            Format = Format.Unknown,
            ViewDimension = UnorderedAccessViewDimension.Buffer,
            Buffer = new BufferUnorderedAccessView
            {
                NumElements = MAX_SEGMENTS
            }
        };
        outputUav = device.CreateUnorderedAccessView(outputBuffer, outputUavDesc);

        // Counter buffer (RWByteAddressBuffer)
        BufferDescription counterDesc = new()
        {
            ByteWidth = 4,
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.UnorderedAccess,
            MiscFlags = ResourceOptionFlags.BufferAllowRawViews
        };
        counterBuffer = device.CreateBuffer(counterDesc);

        UnorderedAccessViewDescription counterUavDesc = new()
        {
            Format = Format.R32_Typeless,
            ViewDimension = UnorderedAccessViewDimension.Buffer,
            Buffer = new BufferUnorderedAccessView
            {
                NumElements = 1,
                Flags = BufferUnorderedAccessViewFlags.Raw
            }
        };
        counterUav = device.CreateUnorderedAccessView(counterBuffer, counterUavDesc);

        // Staging readback for output segments
        BufferDescription readbackDesc = new()
        {
            ByteWidth = (uint)(MAX_SEGMENTS * Unsafe.SizeOf<GpuLineSegment>()),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read
        };
        readbackBuffer = device.CreateBuffer(readbackDesc);

        // Staging readback for counter
        BufferDescription counterReadbackDesc = new()
        {
            ByteWidth = 4,
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read
        };
        counterReadbackBuffer = device.CreateBuffer(counterReadbackDesc);
    }

    private void CreateConstantBuffer()
    {
        BufferDescription cbDesc = new()
        {
            ByteWidth = (uint)AlignTo16(Unsafe.SizeOf<ScanParamsCB>()),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ConstantBuffer
        };
        constantBuffer = device.CreateBuffer(cbDesc);
    }

    public void EnsureSrvTexture(int width, int height)
    {
        if (srvTexture != null && srvWidth == width && srvHeight == height)
            return;

        srvView?.Dispose();
        srvTexture?.Dispose();

        Texture2DDescription desc = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource
        };

        srvTexture = device.CreateTexture2D(desc);
        srvView = device.CreateShaderResourceView(srvTexture);
        srvWidth = width;
        srvHeight = height;
    }

    public void CopySourceTexture(ID3D11Texture2D capturedTexture)
    {
        Texture2DDescription desc = capturedTexture.Description;
        EnsureSrvTexture((int)desc.Width, (int)desc.Height);
        context.CopyResource(srvTexture!, capturedTexture);
    }

    [SkipLocalsInit]
    public void UpdateConstants(ScanParamsCB parameters)
    {
        context.UpdateSubresource(parameters, constantBuffer!);
    }

    public void ResetCounter()
    {
        // Clear counter to zero
        uint[] zero = [0];
        context.UpdateSubresource<uint>(zero, counterBuffer!);
    }

    public void Dispatch(int areaWidth, int numRows)
    {
        int groupsPerRow = (areaWidth + GROUP_SIZE - 1) / GROUP_SIZE;
        int totalGroups = groupsPerRow * numRows;

        context.CSSetShader(computeShader!);
        context.CSSetShaderResource(0, srvView!);
        context.CSSetUnorderedAccessViews(0, [outputUav!, counterUav!]);
        context.CSSetConstantBuffer(0, constantBuffer!);

        context.Dispatch((uint)totalGroups, 1, 1);

        // Unbind
        context.CSSetShader(null);
        context.CSSetShaderResource(0, null);
        context.CSSetUnorderedAccessViews(0, [null!, null!]);
    }

    [SkipLocalsInit]
    public unsafe int ReadResults(Span<LineSegment> output)
    {
        // Copy results to staging buffers
        context.CopyResource(counterReadbackBuffer!, counterBuffer!);
        context.CopyResource(readbackBuffer!, outputBuffer!);

        // Read counter
        MappedSubresource counterMap = context.Map(counterReadbackBuffer!, 0,
            MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        int segmentCount;
        try
        {
            segmentCount = Marshal.ReadInt32(counterMap.DataPointer);
        }
        finally
        {
            context.Unmap(counterReadbackBuffer!, 0);
        }

        segmentCount = Math.Min(segmentCount, MAX_SEGMENTS);
        segmentCount = Math.Min(segmentCount, output.Length);

        if (segmentCount == 0)
            return 0;

        // Read segments
        MappedSubresource segMap = context.Map(readbackBuffer!, 0,
            MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            ReadOnlySpan<GpuLineSegment> gpuSegments = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.AsRef<GpuLineSegment>((void*)segMap.DataPointer),
                segmentCount);

            for (int i = 0; i < segmentCount; i++)
            {
                ref readonly GpuLineSegment gs = ref gpuSegments[i];
                output[i] = new LineSegment(gs.XStart, gs.XEnd, gs.Y);
            }
        }
        finally
        {
            context.Unmap(readbackBuffer!, 0);
        }

        return segmentCount;
    }

    public void Dispose()
    {
        computeShader?.Dispose();
        srvView?.Dispose();
        srvTexture?.Dispose();
        outputUav?.Dispose();
        outputBuffer?.Dispose();
        counterUav?.Dispose();
        counterBuffer?.Dispose();
        readbackBuffer?.Dispose();
        counterReadbackBuffer?.Dispose();
        constantBuffer?.Dispose();
    }

    private static int AlignTo16(int value) => (value + 15) & ~15;
}
