#if UNITY_SWITCH

using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Switch;

namespace sdsg.NintendoSwitch
{
    public class NetworkManager
    {

#if !UNITY_EDITOR
        private static bool refreshing = false;
        private static int requestCount;
        private const float disableTimeout = 30; // Timeout in seconds for when disabling networking.

        /// <summary>
        /// Gets whether the network connection is available for use.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                return NetworkInterfaceWrapper.IsNetworkAvailable();
            }
        }

        /// <summary>
        /// Specifies whether to use a local area network when submitting network use requests.
        /// </summary>
        /// <remarks>
        /// This mode allows you to do networking on a local area network without Internet access.
        /// Although the Internet can still be accessed when it is available on the connected network,
        /// there are side effects such as not being able to agree to the terms of service on public Wi-Fi connections. 
        /// </remarks>
        public static bool IsLocalNetworking { get; set; } = false;

        /// <summary>
        /// Specifies whether to display the error viewer applet when a network use request fails.
        /// If a network use request has already been submitted, you must call <see cref="Refresh"/> before any changes will take effect.
        /// </summary>
        /// <remarks>
        /// In general, this should be set to true for network communication that is required to progress in the scene,
        /// and set to false for background network communication that does not affect the user's ability to progress.
        /// </remarks>
        public static bool IsSilentNetworking
        {
            get
            {
                return NetworkInterfaceWrapper.IsNetworkConnectingOnBackground();
            }
            set
            {
                NetworkInterfaceWrapper.SetNetworkConnectingOnBackground(value);
            }
        }

        /// <summary>
        /// Attempts to restore existing network use requests.
        /// This method does nothing if no network use requests have been submitted.
        /// </summary>
        /// <remarks>
        /// This cancels all outstanding network use requests and resubmits them.
        /// If silent networking is disabled, the user will be prompted to resolve 
        /// issues preventing the use of networking.
        /// 
        /// This method is not thread safe. 
        /// </remarks>
        public static IEnumerator Refresh()
        {
            // Wait for any pending refresh requests to complete
            while (refreshing)
            {
                yield return null;
            }

            // Set the flag to prevent concurrent refresh operations
            refreshing = true;

            // GUIDELINE 0134: Notifying the user when a network use request was refused
            // In Unity, you must cancel all outstanding network use requests and then resubmit in order to display the required notification.
            // The logic below shows one way of doing this.

            requestCount = NetworkInterfaceWrapper.GetNetworkReferenceCount();

            // Cancel all outstanding network use requests
            while (NetworkInterfaceWrapper.GetNetworkReferenceCount() > 0)
            {
                Debug.LogFormat("[NSNM] Cancelling network use request ({0} left).", NetworkInterfaceWrapper.GetNetworkReferenceCount());
                NetworkInterfaceWrapper.LeaveNetworkConnecting();
                yield return null;
            }

            // Wait for networking to be finished, or break out if there is a timeout
            float elapsed = 0f;
            while (!NetworkInterfaceWrapper.IsNetworkFinished() && elapsed < disableTimeout)
            {
                elapsed += UnityEngine.Time.deltaTime;
                yield return null;
            }

            if (elapsed > disableTimeout)
            {
                Debug.LogErrorFormat("[NSNM] The disable network operation timed out after {0} seconds)", disableTimeout);
                refreshing = false;
                yield break;
            }

            // Resubmit the network use request(s). This can bring up the error viewer applet if IsSilentNetworking == false.
            for (int i = 0; i < requestCount; ++i)
            {
                Debug.LogFormat("[NSNM] Resubmitting network use request ({0} of {1}).", i + 1, requestCount);
                NetworkInterfaceWrapper.EnterNetworkConnecting(IsLocalNetworking);
                while (NetworkInterfaceWrapper.IsNetworkConnecting())
                {
                    yield return null;
                }
                if (!NetworkInterfaceWrapper.IsNetworkAccepted())
                {
                    // Stop resubmitting if the first request is rejected
                    Debug.LogWarning("[NSNM] Unable to restore network connection");
                    break;
                }
            }

            // Set the flag back to false when the process is done
            refreshing = false;
        }

#else
        #region Stubs for Unity Editor

        public static bool IsAvailable { get { return true; } }
        public static bool IsSilentNetworking { get; set; }
        public static bool IsLocalNetworking { get; set; } = false;
        public static IEnumerator Refresh() { yield return null; }
        private static int nn_socket_RegisterForStatistics(string processName) { return 0; }

        #endregion
#endif
    }
}
#endif
