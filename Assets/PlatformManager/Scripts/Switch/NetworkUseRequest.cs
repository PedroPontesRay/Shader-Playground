#if UNITY_SWITCH

using System.Collections;
using UnityEngine;
using UnityEngine.Switch;

namespace sdsg.NintendoSwitch
{
    /// <summary>
    /// Suspends coroutine execution until the network use request is processed by the system.
    /// </summary>
    public class NetworkUseRequest : CustomYieldInstruction
    {
#if !UNITY_EDITOR
        private bool submitted;     // Keeps track of whether a request has already been accepted for this instance

        /// <summary>
        /// Use this instance to submit a network use request to the system.
        /// </summary>
        public NetworkUseRequest()
        {
            submitted = false;
        }

        /// <summary>
        /// This will not run until the scene is unloaded, memory runs low, or <see cref="System.GC.Collect"/> is called.
        /// </summary>
        /// <remarks>
        /// If you do not cancel all network use requests when you no longer need networking,
        /// the system will not be able to download anything in the background, including AOC and patches.
        /// </remarks>
        ~NetworkUseRequest()
        {
            if (submitted)
            {
                Debug.LogFormat("[NetworkUseRequest] Removing a network use request, {0} remaining", NetworkInterfaceWrapper.GetNetworkReferenceCount() - 1);
                NetworkInterfaceWrapper.LeaveNetworkConnecting();
            }
        }

        /// <summary>
        /// Indicates whether the network use request was accepted.
        /// This value is only valid immediately after yielding on this instance or <see cref="Submit"/>.
        /// </summary>
        public bool Accepted
        {
            get
            {
                return NetworkInterfaceWrapper.IsNetworkAccepted();
            }
        }

        public override bool keepWaiting
        {
            get
            {
                if (NetworkInterfaceWrapper.IsNetworkConnecting())
                {
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Submits the network use request to the system, or resubmits the request if one was already submitted in this instance.
        /// Set <see cref="NetworkManager.IsLocalNetworking"/> and <see cref="NetworkManager.IsSilentNetworking"/> before calling this method.
        /// </summary>
        public IEnumerator Submit()
        {
            if (!submitted)
            {
                Debug.LogFormat("[NetworkUseRequest] Submitting a network use request: {0}, {1}",
                    NetworkManager.IsLocalNetworking ? "local communication" : "internet",
                    NetworkManager.IsSilentNetworking ? "suppress errors" : "show errors");
                if (!NetworkInterfaceWrapper.EnterNetworkConnecting(NetworkManager.IsLocalNetworking))
                {
                    Debug.Assert(true, "[NetworkUseRequest] NIFM library not initialized. Enable PlayerSettings > Other Settings > Networking > Initialize NIFM.");
                }
                else
                {
                    submitted = true;
                }
            }
            else
            {
                Debug.LogFormat("[NetworkUseRequest] Resubmitting a network use request: {0}, {1}",
                    NetworkManager.IsLocalNetworking ? "local networking" : "internet",
                    NetworkManager.IsSilentNetworking ? "suppress errors" : "show errors");
                yield return NetworkManager.Refresh();
            }
            yield return this;
        }
#else
        #region Stubs for Unity Editor

        public bool Accepted  { get { return true; } }

        public override bool keepWaiting { get { return false; } }

        public IEnumerator Submit() { yield return null; }

        #endregion
#endif
    }
}
#endif
