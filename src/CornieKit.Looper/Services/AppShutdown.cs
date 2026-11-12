using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CornieKit.Looper.Services;

/// <summary>
/// Ends the process without running LibVLC teardown.
/// </summary>
/// <remarks>
/// <see cref="LibVLCSharp.Shared.MediaPlayer.Stop"/> and native release join decoder
/// threads. Those threads call back into <c>Dispatcher.Invoke</c> (video format setup
/// and playback events). Joining them on the UI thread deadlocks the window.
/// <see cref="TerminateProcess"/> does not run finalizers or DLL detach, so that join
/// never gets a chance to block quit.
/// </remarks>
internal static class AppShutdown
{
    private static int _exitRequested;

    public static bool IsExitRequested => Volatile.Read(ref _exitRequested) != 0;

    public static void ExitImmediately()
    {
        Volatile.Write(ref _exitRequested, 1);

        // Exit code 0: this is a user quit, not a crash.
        // TerminateProcess does not return when it succeeds on the current process.
        TerminateProcess(GetCurrentProcess(), 0);

        try
        {
            Process.GetCurrentProcess().Kill();
        }
        catch
        {
            // Already exiting, or the process handle could not be terminated.
        }

        // Do not fall through into WPF/LibVLC cleanup if both exits returned.
        Environment.FailFast("CornieKit Looper immediate exit");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
}
