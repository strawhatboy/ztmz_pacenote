using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace ZTMZ.PacenoteTool;

public class WatchedProcess {
    public string Executable { set; get; }
    public string WindowName { set; get; }

    // KBytes, memory larger than this threshold will be considered as a valid process
    public int MemoryThreshold { set; get; }

    public bool MatchProcess(Process p) {
        if (p == null)
            return false;

        if (string.IsNullOrEmpty(Executable))
            return false;

        if (p.ProcessName.ToLower() != Executable.ToLower())
            return false;

        if (string.IsNullOrEmpty(WindowName))
            return true;

        if (p.MainWindowTitle.ToLower().Contains(WindowName.ToLower()))
            return true;

        return false;
    }
}

public class ProcessWatcher : IDisposable
{
    public event Action<string, string> onNewProcess;
    public event Action<string, string> onProcessExit;
    public ConcurrentDictionary<string, WatchedProcess> WatchingProcesses { get; } = new ();

    public ConcurrentDictionary<string, byte> RunningProcesses { get; } = new ();
    Task _worker;
    bool _isWatching = false;
    object _lock = new ();
    public ProcessWatcher(Action<string, string> newProcessHandler, Action<string, string> processExitHandler)
    {
        onNewProcess += newProcessHandler;
        onProcessExit += processExitHandler;
    }

    public void AddToWatch(string executable, string windowName = "", int memoryThreshold = 0)
    {
        if (string.IsNullOrEmpty(executable))
            return;

        WatchingProcesses[executable.ToLower()] = new WatchedProcess() { Executable = executable, WindowName = windowName, MemoryThreshold = memoryThreshold };
    }

    public void StartWatching()
    {
        lock(_lock) {
            if (_isWatching)
            {
                return;
            }
            _isWatching = true;
        }

        // current processes
        foreach (var p in Process.GetProcesses()) 
        {
            if (WatchingProcesses.ContainsKey(p.ProcessName.ToLower()))
            {
                onNewProcess?.Invoke(p.ProcessName.ToLower(), null);
            }

            RunningProcesses.AddOrUpdate(p.ProcessName.ToLower(), 1, (k, v) => 1);
        }

        _worker = Task.Run(() => {
            while (true) {
                    
                lock(_lock) {
                    if (!_isWatching)
                    {
                        break;
                    }
                }
                var processes = Process.GetProcesses();
                // loop the processes, to raise new process event and exit event
                foreach (var p in processes) 
                {
                    if (!RunningProcesses.ContainsKey(p.ProcessName.ToLower()))
                    {
                        onNewProcess?.Invoke(p.ProcessName.ToLower(), null);
                    }
                }
                Thread.Sleep(2000);
            }
        });
    }

    public void StopWatching()
    {
        // stop all tasks
        lock(_lock) {
            _isWatching = false;
        }
    }

    public void Dispose()
    {
        _worker?.Dispose();
    }
}

