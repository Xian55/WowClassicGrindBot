using Microsoft.Extensions.Options;

using SharedLib;

using System;
using System.Diagnostics;
using System.Threading;

#nullable enable

namespace Game;

public sealed class WowProcess
{
    private static readonly string[] defaultProcessNames = [
        "Wow",
        "WowClassic",
        "WowClassicT",
        "Wow-64",
        "WowClassicB",
        // Windows on ARM64 (e.g. WoW running natively inside a Win11 ARM64 VM
        // on Apple Silicon). Match is OrdinalIgnoreCase, so casing is irrelevant.
        "WowClassic-arm64"
    ];

    private readonly Thread thread;
    private readonly CancellationToken token;

    public Version FileVersion { get; private set; }

    public string Path { get; private set; }

    private Process process;

    private int id = -1;
    public int Id
    {
        get => id;
        set
        {
            id = value;
            process = Process.GetProcessById(id);
        }
    }

    public string ProcessName => process.ProcessName;

    public IntPtr MainWindowHandle => process.MainWindowHandle;

    public bool IsRunning { get; private set; }

    private WowProcess(CancellationTokenSource cts, int pid = -1)
    {
        token = cts.Token;

        Process? p = Get(pid)
            ?? throw new NullReferenceException(
                $"Unable to find {(pid == -1 ? "any" : $"pid={pid}")} " +
                $"running World of Warcraft process!");

        process = p;
        id = process.Id;
        IsRunning = true;
        (Path, FileVersion) = GetProcessInfo(process);

        thread = new(PollProcessExited);
        thread.Start();
    }

    public WowProcess(CancellationTokenSource cts, IOptions<StartupConfigPid> options) : this(cts, options.Value.Id) { }

    private void PollProcessExited()
    {
        while (!token.IsCancellationRequested)
        {
            process.Refresh();
            if (process.HasExited)
            {
                IsRunning = false;

                Process? p = Get();
                if (p != null && TryGetProcessInfo(p, out string path, out Version version))
                {
                    process = p;
                    id = process.Id;
                    Path = path;
                    FileVersion = version;
                    IsRunning = true;
                }
            }

            token.WaitHandle.WaitOne(5000);
        }
    }

    public static Process? Get(int processId = -1)
    {
        if (processId != -1)
        {
            return Process.GetProcessById(processId);
        }

        Process[] processList = Process.GetProcesses();
        for (int i = 0; i < processList.Length; i++)
        {
            Process p = processList[i];
            for (int j = 0; j < defaultProcessNames.Length; j++)
            {
                if (defaultProcessNames[j].Contains(p.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
        }

        return null;
    }

    // Runs on the poll thread, where an escaping exception would take the whole host down.
    // A client that cannot be read (still starting, or started as Administrator)
    // stays not-running and is retried on the next tick.
    private static bool TryGetProcessInfo(Process process, out string path, out Version version)
    {
        try
        {
            (path, version) = GetProcessInfo(process);
            return true;
        }
        catch (Exception)
        {
            path = string.Empty;
            version = new Version();
            return false;
        }
    }

    private static (string path, Version version) GetProcessInfo(Process process)
    {
        // ExecutablePath.Get reports an unreadable process as an empty string, not null.
        // Joining that with the file name yields a relative path, which would resolve
        // against the working directory and look for the game inside the
        // BlazorServer/HeadlessServer folder.
        string path = WinAPI.ExecutablePath.Get(process);
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException(
                $"Unable to read the install directory of the running World of Warcraft process " +
                $"'{process.ProcessName}' (pid={process.Id})! " +
                "This usually means the game was started as Administrator while BlazorServer/HeadlessServer was not. " +
                "Start both with the same privilege level.");
        }

        var exePath = System.IO.Path.Join(path, process.ProcessName + ".exe");
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);

        if (info.FileMajorPart > 0)
        {
            Version v = new(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);

            v = CorrectVersion(v);

            return (path, v);
        }

        return (path, new Version());
    }

    // Blizzard occasionally ships executables with broken file versions
    // where FileMajorPart encodes realMajor*100+realMinor (e.g. 115 means
    // Major=1, Minor=15), FileMinorPart is the real Build, and
    // FileBuildPart*10+FilePrivatePart gives the real Revision.
    private static Version CorrectVersion(Version v)
    {
        if (v.Major < 100)
            return v;

        return new Version(
            v.Major / 100,
            v.Major % 100,
            v.Minor,
            v.Build * 10 + v.Revision);
    }
}