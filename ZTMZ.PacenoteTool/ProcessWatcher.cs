using System;
using System.Management;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace ZTMZ.PacenoteTool;

class ProcessWatcher : IDisposable
{
    public event Action<string, string> onNewProcess;
    public event Action<string, string> onProcessExit;
    public List<string> WatchingProcesses { get; } = new();
    ManagementEventWatcher _creationWatcher = new ManagementEventWatcher();
    ManagementEventWatcher _deletionWatcher = new ManagementEventWatcher();
    List<BackgroundWorker> workers = new();
    bool _isWatching = false;
    public ProcessWatcher(Action<string, string> newProcessHandler, Action<string, string> processExitHandler)
    {
        onNewProcess += newProcessHandler;
        onProcessExit += processExitHandler;
    }

    public void AddToWatch(string processName)
    {
        if (string.IsNullOrEmpty(processName))
            return;

        WatchingProcesses.Add(processName.ToLower());
    }

    public void StartWatching()
    {
        if (_isWatching)
        {
            return;
        }
        _isWatching = true;

        // current processes
        foreach (var p in Process.GetProcesses()) 
        {
            foreach (var wp in WatchingProcesses) 
            {
                if (wp.Contains(p.ProcessName.ToLower()))
                {
                    onNewProcess?.Invoke(wp, null);
                }
            }
        }

        initializeWatcher(_creationWatcher, "__InstanceCreationEvent", onNewProcess);
        initializeWatcher(_deletionWatcher, "__InstanceDeletionEvent", onProcessExit);
    }

    public void StopWatching()
    {
        workers.ForEach(w => w.CancelAsync());
        _isWatching = false;
        _creationWatcher.Stop();
        _deletionWatcher.Stop();
    }

    private void initializeWatcher(ManagementEventWatcher watcher, string evt, Action<string, string> raisedAction)
    {
        WqlEventQuery query = new WqlEventQuery(evt,
            new TimeSpan(0, 0, 5),
            "TargetInstance isa \"Win32_Process\"");

        watcher.Query = query;
        watcher.Options.Timeout = new TimeSpan(0, 0, 5);

        BackgroundWorker bgw = new BackgroundWorker();
        workers.Add(bgw);
        bgw.DoWork += (e, a) =>
        {
            while (_isWatching)
            {
                try
                {
                    ManagementBaseObject mbo = watcher.WaitForNextEvent();
                    var targetInstance = (ManagementBaseObject)mbo["TargetInstance"];
                    var targetInstanceName = targetInstance["Name"].ToString().ToLower();
                    var targetInstanceExecutablePath = targetInstance["ExecutablePath"] as string;
                    if (WatchingProcesses.Contains(targetInstanceName))
                    {
                        raisedAction?.Invoke(targetInstanceName, targetInstanceExecutablePath);
                    }
                }
                catch (System.Management.ManagementException mex) 
                {
                    
                }
            }
        };
        bgw.RunWorkerAsync();
    }

    public void Dispose()
    {
        _creationWatcher.Dispose();
        _deletionWatcher.Dispose();
        workers.ForEach(w => w.Dispose());
    }
}

