#nullable enable
using System;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Opens Play in Headset's address in Quest Browser on the headset (<see cref="HeadsetPlay"/>): the same view
    /// intent, data and package the device tool's <c>preview</c> opens a preview with (<c>tooling/quest/device.ts</c>),
    /// so the page opens as it does there. Only an address <see cref="HeadsetPlay.IsOpenable"/> admits is opened.
    /// Elsewhere, the editor among them, nothing opens: a preview on the computer is Play on Laptop's.
    /// </summary>
    public static class HeadsetBrowser
    {
        /// <summary>Quest Browser's package.</summary>
        public const string BrowserPackage = "com.oculus.browser";

        /// <summary>
        /// Opens <paramref name="address"/> in Quest Browser; false where it is not an address Play in Headset may
        /// open, where this is not the headset, or where the system refused the intent (no Quest Browser, for one).
        /// </summary>
        public static bool Open(Uri? address)
        {
            if (!HeadsetPlay.IsOpenable(address)) return false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var activity = UnityEngine.Android.AndroidApplication.currentActivity;
                using var uri = new AndroidJavaClass("android.net.Uri");
                using var data = uri.CallStatic<AndroidJavaObject>("parse", address!.AbsoluteUri);
                using var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW", data);
                intent.Call<AndroidJavaObject>("setPackage", BrowserPackage)?.Dispose();
                // FLAG_ACTIVITY_NEW_TASK: the browser opens as its own task, as `am start` opens it.
                intent.Call<AndroidJavaObject>("addFlags", 0x10000000)?.Dispose();
                activity.Call("startActivity", intent);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
#else
            return false;
#endif
        }
    }
}
