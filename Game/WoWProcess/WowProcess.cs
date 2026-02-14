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
        "WowClassicB"
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
        (Path, FileVersion) = GetProcessInfo();

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
                if (p != null)
                {
                    process = p;
                    id = process.Id;
                    IsRunning = true;
                    (Path, FileVersion) = GetProcessInfo();
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

    private (string path, Version version) GetProcessInfo()
    {
        string path = WinAPI.ExecutablePath.Get(process)
            ?? throw new NullReferenceException("Unable to identify World of Warcraft process path!");

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