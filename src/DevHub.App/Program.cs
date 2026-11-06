using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace DevHub.App;

/// <summary>
/// Custom entry point (replaces the XAML-generated one) so DevHub runs as a single instance:
/// a second launch, such as a taskbar jump list entry, hands its arguments to the running
/// window and exits.
/// </summary>
public static class Program
{
    private const string InstanceKey = "main";

    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (TryRedirectToRunningInstance())
        {
            return;
        }

        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    private static bool TryRedirectToRunningInstance()
    {
        var mainInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (mainInstance.IsCurrent)
        {
            return false;
        }

        // Let the running instance bring its window to the front (Windows only allows
        // that for a process the foreground one hands it to).
        AllowSetForegroundWindow(mainInstance.ProcessId);

        // Main can't await; run the redirect off this STA thread and wait for it.
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        Task.Run(() => mainInstance.RedirectActivationToAsync(activation).AsTask()).Wait();
        return true;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
