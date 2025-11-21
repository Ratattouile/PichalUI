using System;
using System.Linq;
using DualSenseAPI;
using DualSenseAPI.State;

namespace PichalUI
{
    public class PlayStationController
    {
        private DualSense? _ds; // Nullable
        private bool _running = false;

        public event Action<DualSenseInputState>? StateChanged;

        public bool Start()
        {
            // FirstOrDefault pode retornar null
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
            StateChanged?.Invoke(ds.InputState);
        }

        public void Stop()
        {
            if (!_running || _ds == null) return;
            _running = false;
            try { _ds.EndPolling(); _ds.Release(); } catch { }
        }
    }
}