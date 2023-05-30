using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace ZTMZ.PacenoteTool.Base;

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

        if (!string.IsNullOrEmpty(WindowName)) {
            try {
                // match the window name
                if (!p.MainWindowTitle.ToLower().Contains(WindowName.ToLower()))
                    return false;
            } catch (Exception ex) {
            }
        }

        p.Refresh();
        if (MemoryThreshold > 0) {
            if (p.PrivateMemorySize64 < MemoryThreshold * 1024L) {
                return false;
            }
        }

        return true;
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

    int _refreshInterval = 2000;    // ms

    public ProcessWatcher(int refreshInterval = 2000) {
        this._refreshInterval = refreshInterval;
    }
    public ProcessWatcher(Action<string, string> newProcessHandler, Action<string, string> processExitHandler, int refreshInterval = 2000) : this(refreshInterval)
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
                        if (WatchingProcesses.ContainsKey(p.ProcessName.ToLower()) && 
                            WatchingProcesses[p.ProcessName.ToLower()].MatchProcess(p))
                        {
                            onNewProcess?.Invoke(p.ProcessName.ToLower(), null);
                        }
                        RunningProcesses.AddOrUpdate(p.ProcessName.ToLower(), 1, (k, v) => 1);
                    }
                }

                // loop the running processes, to raise exit event
                foreach (var p in RunningProcesses.Keys.ToList())
                {
                    if (!processes.Any(x => x.ProcessName.ToLower() == p))
                    {
                        if (WatchingProcesses.ContainsKey(p)) {
                            // raise only when watching
                            onProcessExit?.Invoke(p, null);
                        }
                        RunningProcesses.TryRemove(p, out _);
                    }
                }
                Thread.Sleep(this._refreshInterval);
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

