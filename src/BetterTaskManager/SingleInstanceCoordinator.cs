using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BetterTaskManager
{
    internal sealed class SingleInstanceCoordinator : IDisposable
    {
        private const int SwRestore = 9;
        private readonly Mutex instanceMutex;
        private readonly EventWaitHandle activationEvent;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly bool ownsMutex;
        private Task activationTask;
        private bool disposed;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr windowHandle, int commandShow);

        public SingleInstanceCoordinator(string instanceKey = null)
        {
            string key = string.IsNullOrWhiteSpace(instanceKey) ? CurrentInstanceKey() : instanceKey;
            string mutexName = "Local\\BetterTaskManager-" + key + "-Mutex";
            string eventName = "Local\\BetterTaskManager-" + key + "-Activate";

            bool createdNew;
            instanceMutex = new Mutex(true, mutexName, out createdNew);
            bool acquired = createdNew;
            if (!createdNew)
            {
                try { acquired = instanceMutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
            }
            ownsMutex = acquired;
            activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        }

        public bool IsPrimary => ownsMutex;

        public void SignalExistingInstance()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SingleInstanceCoordinator));
            activationEvent.Set();
        }

        public void Attach(Form form)
        {
            if (!ownsMutex) throw new InvalidOperationException("Only the primary instance can attach a window.");
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (activationTask != null) throw new InvalidOperationException("A window is already attached.");

            activationTask = Task.Run(() =>
            {
                WaitHandle[] handles = { activationEvent, cancellation.Token.WaitHandle };
                while (!cancellation.IsCancellationRequested)
                {
                    int signaled = WaitHandle.WaitAny(handles);
                    if (signaled != 0 || cancellation.IsCancellationRequested) break;
                    ActivateWindow(form);
                }
            });
        }

        private static void ActivateWindow(Form form)
        {
            try
            {
                if (form.IsDisposed || !form.IsHandleCreated) return;
                form.BeginInvoke(new Action(() =>
                {
                    if (form.IsDisposed) return;
                    if (form.WindowState == FormWindowState.Minimized)
                    {
                        ShowWindowAsync(form.Handle, SwRestore);
                        form.WindowState = FormWindowState.Normal;
                    }
                    if (!form.Visible) form.Show();
                    form.BringToFront();
                    form.Activate();
                    SetForegroundWindow(form.Handle);
                }));
            }
            catch (InvalidOperationException) { }
        }

        internal static string InstanceKey(string userIdentity, int sessionId)
        {
            string scope = (userIdentity ?? "unknown") + "|" + sessionId.ToString(CultureInfo.InvariantCulture);
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(scope));
                return Convert.ToHexString(hash.AsSpan(0, 12));
            }
        }

        private static string CurrentInstanceKey()
        {
            string identity;
            using (WindowsIdentity current = WindowsIdentity.GetCurrent())
            {
                identity = current.User == null ? Environment.UserName : current.User.Value;
            }
            int sessionId;
            using (Process process = Process.GetCurrentProcess()) sessionId = process.SessionId;
            return InstanceKey(identity, sessionId);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            cancellation.Cancel();
            try { activationEvent.Set(); } catch (ObjectDisposedException) { }
            try { if (activationTask != null) activationTask.Wait(1000); } catch (AggregateException) { }
            activationEvent.Dispose();
            cancellation.Dispose();
            if (ownsMutex)
            {
                try { instanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
            }
            instanceMutex.Dispose();
        }
    }
}
