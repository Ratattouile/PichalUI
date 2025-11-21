using System;
using System.Linq;
using System.Threading;
using DualSenseAPI;
using DualSenseAPI.State;

public class PlayStationController
{
     private DualSense _ds;
    private bool _running = false;

    public event Action<DualSenseInputState>? StateChanged;

    public bool Start()
    {
        _ds = DualSense.EnumerateControllers().FirstOrDefault();
        if (_ds == null) return false;

        _ds.Acquire();
        _ds.OnStatePolled += Ds_OnStatePolled;

        _ds.BeginPolling(8);
        _running = true;
        return true;
    }

    private void Ds_OnStatePolled(DualSense ds)
    {
        // envia o estado para o XAML.cs
        StateChanged?.Invoke(ds.InputState);
    }

    public void Stop()
    {
        if (!_running) return;

        _running = false;

        try { _ds.EndPolling(); } catch { }
        try { _ds.OnStatePolled -= Ds_OnStatePolled; } catch { }
        try { _ds.Release(); } catch { }
    }
}
