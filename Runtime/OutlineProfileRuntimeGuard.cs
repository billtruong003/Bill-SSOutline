#if BILL_OUTLINE_PROFILE_GUARD
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BillSSOutline
{
    /// <summary>
    /// Opt-in (define <c>BILL_OUTLINE_PROFILE_GUARD</c>): makes every scene Volume work on a runtime
    /// clone of its profile during Play Mode. Touching <c>Volume.profile</c> swaps the volume onto an
    /// instantiated copy, so any write during play (code, debug tools, an inspector tweak) lands on
    /// the clone and the profile asset on disk never changes.
    /// </summary>
    public static class OutlineProfileRuntimeGuard
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            // With domain reload off a static subscription survives Play Mode exit and would stack
            // one more callback on every later session.
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            CloneLoadedVolumes();
            // Idempotent by construction: unsubscribe-then-subscribe can never register twice, no
            // matter how the session was entered.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => CloneLoadedVolumes();

        private static void CloneLoadedVolumes()
        {
            foreach (Volume volume in Object.FindObjectsByType<Volume>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (volume.sharedProfile == null || volume.HasInstantiatedProfile()) continue;

                // The getter clones sharedProfile into an instantiated per-volume profile and makes
                // the volume evaluate from it. That single touch is the whole guard.
                _ = volume.profile;
            }
        }
    }
}
#endif
