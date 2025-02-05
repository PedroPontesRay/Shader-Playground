#if UNITY_SWITCH

using System;
using System.Collections;
using UnityEngine;

namespace sdsg.NintendoSwitch
{
    /// <summary>
    /// Suspends coroutine execution until an NSA ID token for the specified account is verified and optionally acquired.
    /// </summary>
    /// <remarks>
    /// GUIDELINE 0168: Prohibition of requesting NSA ID tokens in applications that do not need to get NSA ID tokens
    /// Do not instantiate this class if you do not connect to an independent server.
    /// </remarks>
    public class NsaIdTokenRequest : CustomYieldInstruction
    {
        private nn.Result result;
        private UserAccount account;
        private nn.account.NetworkServiceAccountId nsaId;
        private nn.account.AsyncContext context;
        private RequestState state = RequestState.NotStarted;
        private bool isAsyncDone = false;
        private readonly bool useToken;
        private long tokenLength = 0;
        private readonly byte[] tokenBuffer;
        private byte[] trimmedToken;

        /// <summary>
        /// Use this instance to request an NSA ID token for the specified account and specify whether the token will be used.
        /// </summary>
        /// <param name="account">The account for which to get the NSA ID token.</param>
        /// <param name="useToken">Whether the token will be used by the application (can allocate when 'true').</param>
        public NsaIdTokenRequest(UserAccount account, bool useToken)
        {
            this.account = account;
            this.nsaId = new nn.account.NetworkServiceAccountId();
            this.useToken = useToken;
            context = new nn.account.AsyncContext();
            if (this.useToken)
            {
                tokenBuffer = new byte[nn.account.NetworkServiceAccount.IdTokenLengthMax];
            }
        }

        /// <summary>
        /// Use this instance to request an NSA ID token for the Primary account and load the token data.
        /// </summary>
        public NsaIdTokenRequest() : this (SwitchPlatform.AccountIni, true) { }

        /// <summary>
        /// Use this instance to request an NSA ID token for the Primary account and specify whether the token will be used.
        /// </summary>
        /// <param name="useToken">Whether the token will be used by the application (can allocate when 'true').</param>
        public NsaIdTokenRequest(bool useToken) : this(SwitchPlatform.AccountIni, useToken)
        {
        }

        /// <summary>
        /// Use this instance to request an NSA ID token for the specified account and load the token data.
        /// </summary>
        /// <param name="account">The account for which to get the NSA ID token.</param>
        public NsaIdTokenRequest(UserAccount account) : this(account, true) { }

        /// <summary>
        /// Enumerates the possible states of the request.
        /// </summary>
        private enum RequestState
        {
            NotStarted,                 // The request has not been submitted yet.
            VerifyingNsa,               // Making sure the network service account is valid.
            WaitingForCache,            // Waiting for server-side authentication and allocation of system cache.
            LoadingCache,               // Loading the token from the system cache into application memory.
            Complete                    // Processing is complete and RequestResult contains the result.
        }

        /// <summary>
        /// Gets the result of the request after yielding on the instance or <see cref="Submit"/>.
        /// </summary>
        public NsaResult RequestResult { get; private set; } = NsaResult.Incomplete;

        /// <summary>
        /// Gets the NSA ID of the user account after the request is complete.
        /// Only user acccounts with a linked Nintendo Account will have an NSA ID.
        /// The user account must be open to get the NSA ID.
        /// </summary>
        /// <remarks>
        /// GUIDELINE 0120: Prohibition of displaying the network service account's internal ID
        /// Do not display this ID where users can see it.
        /// </remarks>
        public nn.account.NetworkServiceAccountId NsaId
        {
            get
            {
                if (state != RequestState.Complete)
                {
                    Debug.LogErrorFormat("[NsaIdTokenRequest] NSA ID request not yet complete: {0}", account.ToString());
                }
                else if (RequestResult != NsaResult.Success)
                {
                    Debug.LogErrorFormat("[NsaIdTokenRequest] NSA ID is not available: {0}", account.ToString());
                }

                return nsaId;
            }
        }

        /// <summary>
        /// Gets the NSA ID token data if useToken was set to 'true' when instantiating the NsaIdTokenRequest.
        /// This value is only valid if <see cref="RequestResult"/> == <see cref="NsaResult"/>.Success after yielding on this instance or <see cref="Submit"/>.
        /// </summary>
        public byte[] Token
        {
            get
            {
                if (state != RequestState.Complete)
                {
                    Debug.LogErrorFormat("[NsaIdTokenRequest] Token accessed before request was complete. Current state: {0}", state.ToString());
                    return null;
                }

                return trimmedToken;
            }
        }

        public override bool keepWaiting
        {
            get
            {
                switch (state)
                {
                    case RequestState.NotStarted:
                        // Process hasn't started yet
                        Debug.Log("[NsaIdTokenRequest] Yielding on a request that hasn't been sent. Please call Submit() before yielding.");
                        break;
                    case RequestState.VerifyingNsa:
                        // Verifying there is a valid Network Service Account
                        VerifyNetworkServiceAccount();
                        break;
                    case RequestState.WaitingForCache:
                        // Waiting for async EnsurIdTokenCacheAsync call to finish
                        context.HasDone(ref isAsyncDone);

                        if (isAsyncDone)
                        {
                            CheckAsyncResult();
                        }
                        break;
                    case RequestState.LoadingCache:
                        // Loading token into the prepared cache
                        LoadNsaIdTokenIntoCache();
                        break;
                }

                if (state == RequestState.Complete)
                {
                    // Process is complete
                    if (!UnityEngine.Switch.NetworkInterfaceWrapper.IsNetworkConnectingOnBackground() &&
                        (RequestResult == NsaResult.UnknownAuthenticationError || RequestResult == NsaResult.NetworkCommunicationError))
                    {
                        // GUIDELINE 0135: Handling Result values returned by account library
                        // Whenever you get a ResultNetworkCommunicationError value from the account library, you must display it in the error viewer
                        // unless you implement the logic required for guideline 0134, as demonstrated in NetworkManager.Refresh().

                        // Show NetworkCommunicationError and UnknownAuthenticationError in error viewer if not background networking
                        nn.err.Error.Show(result);
                    }
                }

                return state != RequestState.Complete;
            }
        }

        /// <summary>
        /// Starts the asynchronous request for an NSA ID token. Yield on this method or the instance before checking the result.
        /// </summary>
        public IEnumerator Submit()
        {
            while (state != RequestState.NotStarted && state != RequestState.Complete)
            {
                Debug.LogFormat("[NsaIdTokenRequest] Waiting for previous request to complete: {0}", account.ToString());
                yield return null;
            }

            Debug.LogFormat("[NsaIdTokenRequest] Requesting NSA ID token for {0}", account.ToString());
            state = RequestState.VerifyingNsa;
            yield return this;
        }

        /// <summary>
        /// Verifies that the account has a valid linked Nintendo Account that is available for use.
        /// This method only prompts the user to fix the issue if silent networking is disabled.
        /// </summary>
        /// <returns>Returns the result of the verification process.</returns>
        private NsaResult VerifyLinkedNintendoAccount()
        {
            if (UnityEngine.Switch.NetworkInterfaceWrapper.IsNetworkConnectingOnBackground())
            {
                // Silent mode enabled; check for linked Nintendo Account without prompting user
                bool available = false;
                result = nn.account.NetworkServiceAccount.IsAvailable(ref available, account.Handle);
                if (!result.IsSuccess())
                {
                    Debug.LogErrorFormat("[NsaIdTokenRequest] An error occurred while checking availability of the NSA: {0}", result.ToString());

                    return NsaResult.ImplementationError;
                }

                if (!available)
                {
                    Debug.LogFormat("[NsaIdTokenRequest] Network Service Account is not available for use: {0}", account.ToString());

                    return NsaResult.NsaNotAvailable;
                }
            }
            else
            {
                // Silent mode disabled; display error applet so that user can try to fix any issues

                result = nn.account.NetworkServiceAccount.EnsureAvailable(account.Handle);
                if (!result.IsSuccess())
                {
                    if (nn.account.Account.ResultCancelledByUser.Includes(result))
                    {
                        // User did not attempt to resolve the issue
                        Debug.LogFormat("[NsaIdTokenRequest] Clicked the Cancel button at the prompt: {0}", account.ToString());

                        return NsaResult.NsaNotAvailable;
                    }
                    else
                    {
                        // This generally should not happen
                        Debug.LogErrorFormat("[NsaIdTokenRequest] An error occured while attempting to ensure the NSA: {0}", result.ToString());

                        return NsaResult.UnknownAuthenticationError;
                    }
                }
            }

            // The NSA account was successfully verified, so get the NSA ID
            result = nn.account.NetworkServiceAccount.GetId(ref nsaId, account.Handle);
            if (nn.account.NetworkServiceAccount.ResultNetworkServiceAccountUnavailable.Includes(result))
            {
                return NsaResult.NsaNotAvailable;
            }

            Debug.LogFormat("[NsaIdTokenRequest] Ensured availability of NSA ID {0}", nsaId.ToString().ToLower());
            return NsaResult.Success;
        }

        /// <summary>
        /// Verifies that the user account is in a valid state for acquiring an NSA ID token 
        /// and starts the asynchronous process to authenticate the account and download the token to the system.
        /// </summary>
        private void VerifyNetworkServiceAccount()
        {
            Debug.LogError("ENSUR ID TOKEN");
            // Make sure the account is open
            if (!account.IsOpen)
            {
                Debug.LogErrorFormat("[NsaIdTokenRequest] Account must be open to get an NSA ID: {0}", account.ToString());
                RequestResult = NsaResult.ImplementationError;

                state = RequestState.Complete;
                return;
            }

            // Verify that we are able to start the async process to allocate a cache for the NSA ID token
            while (state != RequestState.Complete)
            {
                result = nn.account.NetworkServiceAccount.EnsureIdTokenCacheAsync(context, account.Handle);

                if (result.IsSuccess())
                {
                    RequestResult = NsaResult.Success;

                    // Start waiting for the async operation to complete
                    state = RequestState.WaitingForCache;
                    return;
                }
                else
                {
                    if (nn.account.NetworkServiceAccount.ResultNetworkServiceAccountUnavailable.Includes(result))
                    {
                        // GUIDELINE 0121: Implementing handling when your application is unable to use a network service account
                        // Whenever you get a ResultNetworkServiceAccountUnavailable value from an SDK function, 
                        // you must call nn.account.NetworkServiceAccount.EnsureAvailable to display the recovery sequence.

                        // There is no linked Nintendo Account, or the linked account is in an invalid state
                        // Check for a valid linked Nintendo Account

                        RequestResult = VerifyLinkedNintendoAccount();

                        if (RequestResult == NsaResult.Success)
                        {
                            // When EnsureAvailable() returns a successful result, it means the user fixed the issue
                            // Run through another iteration of the loop to try again.
                            continue;
                        }
                        else
                        {
                            // GUIDELINE 0122: Prohibition of automatically retrying to get network service account ID tokens
                            // If EnsureAvailable() returns an unsuccessful result, it means the user did not or could not resolve the issue.
                            // Do not try to get another NSA ID token until the user explicitly chooses an online feature again.

                            Debug.LogFormat("[NsaIdTokenRequest] Network Service Account verification failed for {0}", account.ToString());

                            // GUIDELINE 0208: Prohibition of using Nintendo Account-related terms
                            // Do not display any error messages that include terms such as "Nintendo Account" and "link."
                            // The account management applet tells the user that a Nintendo Account must be linked.
                            // If the user declines, the expected application behavior is to simply return the user to the previous menu without displaying anything.
                            //
                            // If your game flow requires some sort of message to be displayed when the user declines to link a Nintendo Account,
                            // please display a message that does not include any prohibited terms (e.g., "Press the A Button to play online").
                        }
                    }
                    else if (nn.account.NetworkServiceAccount.ResultNetworkCommunicationError.Includes(result))
                    {
                        // This means some kind of network communication error occurred. It can also mean an application update is required.

                        // GUIDELINE 0122: Prohibition of automatically retrying to get network service account ID tokens
                        // Whenever you get a ResultNetworkCommunicationError value from the EnsurIdTokenCacheAsync() method,
                        // you must not automatically retry without a user operation.

                        Debug.LogFormat("[NsaIdTokenRequest] A network communication error occurred while ensuring the NSA ID token for {0}", account.ToString());
                        RequestResult = NsaResult.NetworkCommunicationError;
                    }
                    else
                    {
                        Debug.LogErrorFormat("[NsaIdTokenRequest] Unknown error while trying to ensure the ID token cache: {0}", result.ToString());
                        RequestResult = NsaResult.UnknownAuthenticationError;
                    }

                    // Stop the process if the initial Nintendo Account check was unsuccessful
                    state = RequestState.Complete;
                    return;
                }
            }
        }

        /// <summary>
        /// Checks the result of the asynchronous process started by <see cref="VerifyNetworkServiceAccount"/>.
        /// </summary>
        private void CheckAsyncResult()
        {
            // Get the result of the async operation
            result = context.GetResult();

            if (!result.IsSuccess())
            {
                if (nn.account.NetworkServiceAccount.ResultNetworkServiceAccountUnavailable.Includes(result))
                {
                    // GUIDELINE 0121: Implementing handling when your application is unable to use a network service account
                    // Whenever you get a ResultNetworkServiceAccountUnavailable value from an SDK function, 
                    // you must call nn.account.NetworkServiceAccount.EnsureAvailable() to display the recovery sequence.
                    // However, you can fail silently for background network communication that does not impact gameplay.

                    RequestResult = VerifyLinkedNintendoAccount();

                    // GUIDELINE 0170: Implementing handling for features that require a Nintendo Switch Online membership (When all features require membership)
                    // If the Nintendo Switch Online Policy is set to Applies to All on the OMAS application and the user does not have a membership,
                    // the call to nn.account.NetworkServiceAccount.EnsureAvailable() method will prompt the user to sign up.
                    // NOTE: In the development environment, pressing the A Button in the dummy eShop applet does not actually grant a Nintendo Switch Online membership.
                    //       See the Test Method section of Guideline 0170 for instructions.

                    // If the user signed up, repeat the VerifyingNsa step
                    if (RequestResult == NsaResult.Success)
                    {
                        state = RequestState.VerifyingNsa;
                        return;
                    }

                    Debug.LogFormat("[NsaIdTokenRequest] Network Service Account verification failed for {0}", account.ToString());

                    // GUIDELINE 0208: Prohibition of using Nintendo Account-related terms
                    // The account management applet tells the user that a NSO membership is required.
                    // If the user declines, the expected application behavior is to simply return the user to the previous menu without displaying anything.
                    //
                    // If your game flow requires some sort of message to be displayed when the user declines to sign up for a Nintendo Switch Online membership,
                    // please display a message that does not include any prohibited terms (e.g., "Press the A Button to play online").
                }
                else if (nn.account.NetworkServiceAccount.ResultNetworkCommunicationError.Includes(result))
                {
                    // GUIDELINE 0122: Prohibition of automatically retrying to get network service account ID tokens
                    // Whenever you get a ResultNetworkCommunicationError value when trying to get a NSA ID token,
                    // you must not automatically retry without a user operation.

                    // This means some kind of network communication error occurred. It can also mean an application update is required.
                    Debug.LogFormat("[NsaIdTokenRequest] A network communication error occurred during the async NSA ID token operation for {0}", account.ToString());
                    RequestResult = NsaResult.NetworkCommunicationError;
                }
                else
                {
                    // This should never occur
                    Debug.LogErrorFormat("[NsaIdTokenRequest] Unknown error occurred during the async NSA ID token operation: {0}", result.ToString());
                    RequestResult = NsaResult.UnknownAuthenticationError;
                }

                // Check if the user fixed the issue
                if (RequestResult != NsaResult.Success)
                {
                    state = RequestState.Complete;
                    return;
                }
            }

            if (useToken)
            {
                state = RequestState.LoadingCache;
            }
            else
            {
                state = RequestState.Complete;
            }
        }

        /// <summary>
        /// Loads the NSA ID token into the provided cache.
        /// </summary>
        private void LoadNsaIdTokenIntoCache()
        {
            Debug.LogError("LOADING NSA ID TOKEN");
            result = nn.account.NetworkServiceAccount.LoadIdTokenCache(ref tokenLength, tokenBuffer, account.Handle);
            if (!result.IsSuccess())
            {
                if (nn.account.NetworkServiceAccount.ResultNetworkServiceAccountUnavailable.Includes(result))
                {
                    // This occurs when the NSA becomes unavailable before the Load function is called.
                    Debug.LogErrorFormat("[NsaIdTokenRequest] The network service account is no longer available: {0}", account.ToString());

                    // GUIDELINE 0121: Implementing handling when your application is unable to use a network service account
                    // Whenever you get a ResultNetworkServiceAccountUnavailable value from an SDK function, 
                    // you must call nn.account.NetworkServiceAccount.EnsureAvailable to display the recovery sequence.
                    state = RequestState.VerifyingNsa;
                }
                else if (nn.account.NetworkServiceAccount.ResultTokenCacheUnavailable.Includes(result))
                {
                    // This means the cache expired before the token was loaded into memory
                    Debug.LogErrorFormat("[NsaIdTokenRequest] The token cache expired before the token could be loaded: {0}", account.ToString());

                    // GUIDELINE 0135: Handling Result values returned by account library
                    // If you get a ResultTakenCacheUnavailable value, you must restart the NSA ID token acquisition sequence from the beginning.
                    state = RequestState.VerifyingNsa;
                }
                else
                {
                    Debug.LogErrorFormat("[NsaIdTokenRequest] Unknown error occurred while trying to load the NSA ID token into the cache: {0}", result.ToString());
                    RequestResult = NsaResult.UnknownAuthenticationError;
                    state = RequestState.Complete;
                }

                return;
            }

            // Copy token buffer to a resized byte array with a null value at the end
            trimmedToken = new byte[tokenLength + 1];
            Array.Copy(tokenBuffer, trimmedToken, (int)tokenLength);
            trimmedToken[tokenLength] = 0;

            Debug.LogFormat("[NsaIdTokenRequest] Successfully obtained NSA ID token for {0}", account.ToString());

            state = RequestState.Complete;
        }
    }
}
#endif
