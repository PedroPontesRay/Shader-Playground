#if UNITY_SWITCH

namespace sdsg.NintendoSwitch
{
    /// <summary>
    /// Enumerates the results of an NSA ID Token request.
    /// </summary>
    public enum NsaResult
    {
        Incomplete,                 // The request is not yet complete.
        Success,                    // The request was successful.
        NetworkCommunicationError,  // A network communication error occurred. This is automatically shown in the error viewer applet if NetworkManager.IsSilentNetworking == false.
        NsaNotAvailable,            // The network service account does not exist or is not in a usable state. Do not automatically retry without user action.
        ImplementationError,        // Something is wrong with the implementation (such as attempting to download a token when the UserAccount is closed).
        UnknownAuthenticationError  // An unknown error occurred. This is automatically shown in the error viewer applet if NetworkManager.IsSilentNetworking == false.
    }
}
#endif
