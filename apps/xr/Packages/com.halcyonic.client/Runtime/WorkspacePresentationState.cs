#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>The local workspace presentation, shared by the menu and optional spatial agents.</summary>
    public sealed class WorkspacePresentationState
    {
        private bool changing;
        public bool Minimized { get; private set; }
        public bool AgentsEnabled { get; private set; }
        public bool AgentsVisible => AgentsEnabled && !Minimized;

        /// <summary>Revokes retained input and command authority before any presentation disappears.</summary>
        public event Action? Suspending;
        public event Action? Changed;

        public bool Minimize()
        {
            if (Minimized || changing) return false;
            changing = true;
            try
            {
                Suspending?.Invoke();
                Minimized = true;
                Changed?.Invoke();
                return true;
            }
            finally { changing = false; }
        }

        public bool Restore()
        {
            if (!Minimized || changing) return false;
            changing = true;
            try { Minimized = false; Changed?.Invoke(); return true; }
            finally { changing = false; }
        }

        public bool SetAgentsEnabled(bool enabled)
        {
            if (AgentsEnabled == enabled || changing) return false;
            changing = true;
            try { AgentsEnabled = enabled; Changed?.Invoke(); return true; }
            finally { changing = false; }
        }
    }
}
