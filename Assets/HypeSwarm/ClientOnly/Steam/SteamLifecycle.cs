using System;
using Steamworks;
using UnityEngine;

namespace HypeSwarm.ClientOnly.Steam
{
    /// <summary>
    /// Brings the Steam API up, pumps its callbacks, and takes it down again — and reports plainly
    /// when it cannot, because "cannot" is the normal case in the Editor.
    /// </summary>
    /// <remarks>
    /// Steam being absent is not an error. A developer with Steam closed, a dedicated server on a
    /// VPS (§13.4), and a player who launched the executable directly all reach this and all must
    /// end up on the direct transport with one line in the log rather than an exception. Every
    /// method here is safe to call when initialisation failed.
    ///
    /// <para><c>RestartAppIfNecessary</c> is deliberately not called. It relaunches the process
    /// through Steam, which during development means the Editor's player vanishing and reappearing
    /// under an app id that is not ours. It belongs with the real app id in Phase 5 (§22).</para>
    /// </remarks>
    public static class SteamLifecycle
    {
        /// <summary>
        /// Valve's public test app id. Every Steam account owns it, which is what makes it the
        /// standard stand-in until the game has an id of its own (§22).
        /// </summary>
        public const uint DevelopmentAppId = 480;

        static SteamCallbackPump pump;

        public static bool IsRunning { get; private set; }

        /// <summary>
        /// Puts every static here back to its starting value when play mode begins.
        /// </summary>
        /// <remarks>
        /// This project enters play mode without a domain reload, so these statics survive from the
        /// previous session. Steamworks.NET zeroes its own dispatcher counter on the same callback,
        /// expecting the game to re-initialise — and a stale <see cref="IsRunning"/> is exactly what
        /// stops that happening. The result is the worst kind of broken: Steam reports itself
        /// running and answers <c>GetPersonaName</c> from the still-live native library, while every
        /// <c>RunCallbacks</c> throws, so no callback ever arrives and anything waiting on one waits
        /// forever. That is a button that does nothing on the second press of Play and works
        /// perfectly in a build, where the process is always new.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlayMode()
        {
            IsRunning = false;
            FailureReason = "Steam has not been started yet";
            pump = null;
        }

        /// <summary>Why Steam is not running. Empty while it is.</summary>
        public static string FailureReason { get; private set; } = "Steam has not been started yet";

        /// <summary>This player's Steam id, or <c>CSteamID.Nil</c> when Steam is not running.</summary>
        public static CSteamID LocalUser => IsRunning ? SteamUser.GetSteamID() : CSteamID.Nil;

        /// <summary>This player's Steam persona name, or an empty string.</summary>
        public static string LocalName => IsRunning ? SteamFriends.GetPersonaName() : string.Empty;

        /// <summary>
        /// Starts Steam if it is not already started. Returns whether Steam is usable afterwards, so
        /// callers can treat a first call and a repeat call identically.
        /// </summary>
        public static bool TryStart()
        {
            if (IsRunning)
            {
                return true;
            }

            if (!Packsize.Test())
            {
                return Fail("the Steamworks binaries do not match this platform's struct packing");
            }

            if (!DllCheck.Test())
            {
                return Fail("the Steamworks native libraries are missing or the wrong version");
            }

            string error;

            // InitEx over Init: the same call, but it says what went wrong instead of just false.
            var result = SteamAPI.InitEx(out error);

            if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                return Fail(string.IsNullOrEmpty(error) ? result.ToString() : error);
            }

            // Steam's own init succeeding is not enough: the managed dispatcher is what turns
            // callbacks into C# events, and without it every RunCallbacks throws instead. Checked
            // here so the failure is one line at startup rather than a lobby that never appears.
            if (!CallbackDispatcher.IsInitialized)
            {
                SteamAPI.Shutdown();

                return Fail("Steam initialised but its callback dispatcher did not, so no Steam " +
                            "callback would ever arrive. Restarting the Editor clears this.");
            }

            IsRunning = true;
            FailureReason = string.Empty;

            SteamClient.SetWarningMessageHook();
            EnsurePump();

            Debug.Log($"[Steam] Running as {LocalName} ({LocalUser}).");

            return true;
        }

        public static void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            FailureReason = "Steam was shut down";

            if (pump != null)
            {
                UnityEngine.Object.Destroy(pump.gameObject);
                pump = null;
            }

            SteamAPI.Shutdown();
        }

        static bool Fail(string reason)
        {
            IsRunning = false;
            FailureReason = reason;

            Debug.Log($"[Steam] Not available: {reason}. Falling back to direct connections.");

            return false;
        }

        /// <summary>
        /// Steam's callbacks only fire while something calls <c>RunCallbacks</c>, so initialising
        /// without a pump gives an API that appears to work and never answers.
        /// </summary>
        static void EnsurePump()
        {
            if (pump != null)
            {
                return;
            }

            // HideInHierarchy, not HideAndDontSave: the DontSave flags keep an object alive when
            // play mode ends, so the pump would never reach OnApplicationQuit, Steam would never be
            // shut down, and each session would leave another one behind.
            var host = new GameObject("Steam Callbacks") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(host);

            pump = host.AddComponent<SteamCallbackPump>();
        }

        sealed class SteamCallbackPump : MonoBehaviour
        {
            void Update()
            {
                if (IsRunning)
                {
                    SteamAPI.RunCallbacks();
                }
            }

            void OnApplicationQuit() => Stop();
        }
    }

    /// <summary>Wraps the warning hook so the delegate is not collected while Steam holds it.</summary>
    static class SteamClient
    {
        static SteamAPIWarningMessageHook_t hook;

        public static void SetWarningMessageHook()
        {
            hook = OnWarning;
            SteamUtils.SetWarningMessageHook(hook);
        }

        [AOT.MonoPInvokeCallback(typeof(SteamAPIWarningMessageHook_t))]
        static void OnWarning(int severity, System.Text.StringBuilder message)
        {
            Debug.LogWarning($"[Steam] {message}");
        }
    }
}
