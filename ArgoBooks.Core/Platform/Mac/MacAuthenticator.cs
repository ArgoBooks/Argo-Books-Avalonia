using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace ArgoBooks.Core.Platform.Mac;

/// <summary>
/// Touch ID, via the LocalAuthentication framework. The counterpart to Windows Hello and to
/// <see cref="Linux.LinuxAuthenticator"/>.
///
/// Reached through the Objective-C runtime rather than a binding library, so the app takes on
/// no new dependency for one prompt. Only three calls are needed: allocate an LAContext, ask
/// whether it can evaluate the biometrics policy, and evaluate it.
///
/// The evaluation is asynchronous and answers through an Objective-C block, which has no
/// managed equivalent, so <see cref="BlockLiteral"/> below builds one by hand to the ABI the
/// runtime expects.
/// </summary>
internal static class MacAuthenticator
{
    private const string ObjCRuntime = "/usr/lib/libobjc.dylib";
    private const string Dl = "/usr/lib/libdl.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    private const string LocalAuthenticationFramework =
        "/System/Library/Frameworks/LocalAuthentication.framework/LocalAuthentication";

    /// <summary>
    /// LAPolicyDeviceOwnerAuthenticationWithBiometrics. Biometrics only, deliberately not the
    /// policy that falls back to the account password: this gates a feature the user turned on
    /// as "unlock with Touch ID", and a Mac without Touch ID should report it unavailable
    /// rather than quietly asking for a password instead.
    /// </summary>
    private const nint PolicyBiometrics = 1;

    /// <summary>BLOCK_IS_GLOBAL. Marks a block with no captured state and no lifetime to manage.</summary>
    private const int BlockIsGlobal = 1 << 28;

    [DllImport(ObjCRuntime)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjCRuntime)] private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendMessage(IntPtr receiver, IntPtr selector, string argument);

    [DllImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
    private static extern bool SendCanEvaluate(IntPtr receiver, IntPtr selector, nint policy, out IntPtr error);

    [DllImport(ObjCRuntime, EntryPoint = "objc_msgSend")]
    private static extern void SendEvaluate(IntPtr receiver, IntPtr selector, nint policy, IntPtr reason, IntPtr reply);

    [DllImport(Dl)] private static extern IntPtr dlopen(string path, int mode);
    [DllImport(Dl)] private static extern IntPtr dlsym(IntPtr handle, string symbol);

    /// <summary>The layout the runtime reads a block through. Order and size are fixed by the ABI.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public IntPtr Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }

    /// <summary>void (^)(BOOL success, NSError *error). BOOL is one byte here.</summary>
    private delegate void ReplyHandler(IntPtr block, byte success, IntPtr error);

    // Held in a static so the GC cannot collect the thunk the runtime is about to call into.
    private static readonly ReplyHandler Reply = OnReply;
    private static readonly IntPtr ReplyPointer = Marshal.GetFunctionPointerForDelegate(Reply);

    // Each evaluation gets its own block, which is how a reply finds the call it belongs to.
    private static readonly ConcurrentDictionary<IntPtr, TaskCompletionSource<bool>> Pending = new();

    private static readonly Lazy<bool> FrameworkLoaded = new(() =>
    {
        try
        {
            // RTLD_NOW. The class is not registered with the runtime until the framework is in.
            return dlopen(LocalAuthenticationFramework, 2) != IntPtr.Zero
                   && objc_getClass("LAContext") != IntPtr.Zero;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    });

    /// <summary>
    /// True when this Mac has a biometric sensor with a fingerprint enrolled and the app is
    /// allowed to use it. False on a Mac with no Touch ID, or one where nothing is enrolled.
    /// </summary>
    public static bool IsAvailable()
    {
        if (!OperatingSystem.IsMacOS() || !FrameworkLoaded.Value)
            return false;

        try
        {
            var context = CreateContext();
            if (context == IntPtr.Zero)
                return false;

            try
            {
                return SendCanEvaluate(context, sel_registerName("canEvaluatePolicy:error:"), PolicyBiometrics, out _);
            }
            finally
            {
                Release(context);
            }
        }
        catch (Exception)
        {
            // A runtime that will not answer is one that cannot authenticate either.
            return false;
        }
    }

    /// <summary>
    /// Shows the system Touch ID prompt and resolves once the user answers it.
    /// </summary>
    /// <param name="reason">
    /// Shown in the prompt under "Argo Books is trying to...". Apple requires it to be
    /// non-empty and to say what is being unlocked.
    /// </param>
    /// <returns>True only when the fingerprint was accepted. Cancelling is a false, not a throw.</returns>
    public static async Task<bool> AuthenticateAsync(string reason)
    {
        if (!OperatingSystem.IsMacOS() || !FrameworkLoaded.Value)
            return false;

        if (string.IsNullOrWhiteSpace(reason))
            reason = "Verify your identity";

        IntPtr context = IntPtr.Zero;
        IntPtr block = IntPtr.Zero;
        IntPtr descriptor = IntPtr.Zero;

        try
        {
            context = CreateContext();
            if (context == IntPtr.Zero)
                return false;

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            descriptor = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
            Marshal.StructureToPtr(
                new BlockDescriptor { Reserved = 0, Size = (nuint)Marshal.SizeOf<BlockLiteral>() },
                descriptor, false);

            block = Marshal.AllocHGlobal(Marshal.SizeOf<BlockLiteral>());
            Marshal.StructureToPtr(
                new BlockLiteral
                {
                    Isa = GlobalBlockIsa(),
                    Flags = BlockIsGlobal,
                    Reserved = 0,
                    Invoke = ReplyPointer,
                    Descriptor = descriptor,
                },
                block, false);

            Pending[block] = completion;

            var nsReason = SendMessage(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), reason);
            SendEvaluate(context, sel_registerName("evaluatePolicy:localizedReason:reply:"),
                         PolicyBiometrics, nsReason, block);

            // The prompt has no deadline of its own and sits until answered. This one exists so
            // a reply that never arrives cannot strand the unlock screen forever.
            var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromMinutes(2)));
            if (finished != completion.Task)
            {
                // Dismisses the prompt. LocalAuthentication answers that with a cancel, which
                // OnReply then ignores because the call is no longer pending.
                SendMessage(context, sel_registerName("invalidate"));
                return false;
            }

            return completion.Task.Result;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            // The block and its descriptor are deliberately never freed. LocalAuthentication keeps
            // the block until it replies and releases it afterwards on its own queue, so nothing on
            // this side runs provably after its last use. Freeing it here let an answer that came
            // after the timeout call into freed memory, and even an on-time answer raced the
            // release. It is 48 bytes per prompt, and a global block is meant to live that long.
            if (block != IntPtr.Zero)
                Pending.TryRemove(block, out _);

            if (context != IntPtr.Zero)
                Release(context);
        }
    }

    /// <summary>Says why biometrics are unavailable, in the terms the settings screen uses.</summary>
    public static string DescribeAvailability()
    {
        if (!OperatingSystem.IsMacOS())
            return "Touch ID is only available on macOS.";

        if (!FrameworkLoaded.Value)
            return "The LocalAuthentication framework could not be loaded.";

        return IsAvailable()
            ? "Available"
            : "Touch ID is not set up on this Mac. Add a fingerprint in System Settings > Touch ID & Password.";
    }

    private static void OnReply(IntPtr block, byte success, IntPtr error)
    {
        // Runs on whichever queue LocalAuthentication answers on, so it does nothing but
        // hand the answer back. Anything heavier belongs on the awaiting side.
        if (Pending.TryRemove(block, out var completion))
            completion.TrySetResult(success != 0);
    }

    private static IntPtr CreateContext()
    {
        var contextClass = objc_getClass("LAContext");
        if (contextClass == IntPtr.Zero)
            return IntPtr.Zero;

        return SendMessage(SendMessage(contextClass, sel_registerName("alloc")), sel_registerName("init"));
    }

    private static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
            SendMessage(instance, sel_registerName("release"));
    }

    private static IntPtr GlobalBlockIsa() => dlsym(dlopen(LibSystem, 2), "_NSConcreteGlobalBlock");
}
