using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_GAMECORE
using Unity.GameCore;
using UnityEngine.GameCore;
public class XboxPlatform : IPlatform
{
    const string scid = "00000000-0000-0000-0000-00007a9d8340"; // Xbox Service Config ID
    const int titleID = 0x7a9d8340;
    public const string fileName = "saveData";

    UserManager userManager;
    public static bool userConnected;
    bool onlineServices = true;
    Queue<Callback> callbackQueue;

	#region Behaviours
	void IPlatform.OnPlatformStart()
    {
#if !UNITY_EDITOR
		PlatformManager.instance.gameObject.AddComponent<GamingRuntimeManager>();

		callbackQueue = new Queue<Callback>();

		UnityEngine.WindowsGames.WindowsGamesPLM.OnApplicationSuspendingEvent += GameCorePLM_OnApplicationSuspendingEvent;
		UnityEngine.WindowsGames.WindowsGamesPLM.OnResourceAvailabilityChangedEvent += GameCorePLM_OnResourceAvailabilityChangedEvent;

		if (userManager == null)
		{
			userManager = new UserManager();
               bool result = userManager.AddDefaultUserSilently(new UserManager.AddUserCompletedDelegate((UserManager.UserOpResult userOPResult) =>
			{
               Debug.Log($"[Xbox Platform] Open User Result: {userOPResult.ToString()} ");

				switch (userOPResult)
				{
					case UserManager.UserOpResult.Success:
						userConnected = true;
						break;
					case UserManager.UserOpResult.NoDefaultUser:
					case UserManager.UserOpResult.ResolveUserIssueRequired:
					case UserManager.UserOpResult.UnclearedVetoes:
					case UserManager.UserOpResult.UnknownError:
						// Called on error or closing the selection screen

						if (userManager.CurrentUserData.userHandle != null)
						{
							onlineServices = false;
							goto case UserManager.UserOpResult.Success;
						}

						onlineServices = true;
						userConnected = false;
						userManager.UnregisterCallbacks();
						userManager = null;
						Debug.LogError($"[Xbox Platform] Error while getting user ID: {userOPResult.ToString()}");
						break;
					default:
						break;
				}
               }));
		}

		SDK.XPackageGetCurrentProcessPackageIdentifier(out string identifier);
#endif

    }
    void IPlatform.OnPlatformUpdate()
    {
#if !UNITY_EDITOR
		if (userManager != null)
			userManager.Update();
				
			DequeueCallbacks();
#endif
	}
	#endregion

	#region Save/load
	void IPlatform.SaveGameData(byte[] rawData, string filename, Action<bool> callback)
    {
#if !UNITY_EDITOR
			XGameSaveContainerHandle containerContext;
			XGameSaveUpdateHandle updateContext;

			SDK.XGameSaveInitializeProviderAsync(
				userManager.CurrentUserData.userHandle, 
				scid, 
				false, 
				new XGameSaveInitializeProviderCompleted((int hResult, XGameSaveProviderHandle providerContext) =>
				{
					switch (hResult)
					{
						case Unity.GameCore.HR.S_OK:
							SDK.XGameSaveCreateContainer(providerContext, "SaveContainer", out containerContext);
							SDK.XGameSaveCreateUpdate(containerContext, "SaveUpdate", out updateContext);

							SDK.XGameSaveSubmitBlobWrite(updateContext, filename, rawData);
							SDK.XGameSaveSubmitUpdate(updateContext);

							SDK.XGameSaveCloseUpdateHandle(updateContext);
							SDK.XGameSaveCloseContainer(containerContext);

							callbackQueue.Enqueue(new BoolCallbackData(callback, true));
							break;
						default:
							callbackQueue.Enqueue(new BoolCallbackData(callback, false));
							break;
					}
					SDK.XGameSaveCloseProvider(providerContext);
				}));
#else
        callback?.Invoke(true);
#endif // UNITY_EDITOR
    }
    void IPlatform.LoadGameData(string filename, Action<bool, byte[], string> callback)
    {
#if !UNITY_EDITOR
			XGameSaveContainerHandle containerContext;
			XGameSaveUpdateHandle updateContext;

			string[] blobNames = new string[] { filename };

			SDK.XGameSaveInitializeProviderAsync(
				userManager.CurrentUserData.userHandle, 
				scid, 
				false, 
				new XGameSaveInitializeProviderCompleted((int hProviderResult, XGameSaveProviderHandle providerContext) =>
				{
					switch (hProviderResult)
					{
						case 0:
							SDK.XGameSaveCreateContainer(providerContext, "SaveContainer", out containerContext);
							SDK.XGameSaveCreateUpdate(containerContext, "SaveUpdate", out updateContext);
							// Reading blobs
							SDK.XGameSaveReadBlobDataAsync(containerContext, blobNames, new XGameSaveReadBlobDataCompleted((int hReadResult, XGameSaveBlob[] blobs) =>
							{
								switch (hReadResult)
								{
									case Unity.GameCore.HR.S_OK:
										callbackQueue.Enqueue(new SaveCallbackData(callback, true, blobs[0].Data, filename));
										break;
									case Unity.GameCore.HR.E_GS_BLOB_NOT_FOUND:
										callbackQueue.Enqueue(new SaveCallbackData(callback, true, new byte[0], filename)); // Allows to proceed, since there is no save data
										break;
									default:
										callbackQueue.Enqueue(new SaveCallbackData(callback, false, new byte[0], filename));
										break;
								}
							}));
							SDK.XGameSaveCloseProvider(providerContext);
							break;
						default:
							callbackQueue.Enqueue(new SaveCallbackData(callback, false, new byte[0], filename));
							break;
					}
				}));
#else
        callback?.Invoke(true, new byte[0], filename);
#endif
    }
	#endregion

	#region User
	int IPlatform.GetUserID()
    {
        return (int)userManager.CurrentUserData.userXUID;
    }

    string IPlatform.GetNetworkUserID()
    {
        return userManager.CurrentUserData.userXUID.ToString();
    }
	string IPlatform.GetUniqueID()
	{
		return userManager.CurrentUserData.userXUID.ToString();
	}
	string IPlatform.GetNickname()
	{
#if UNITY_EDITOR
		return "USER_GAMERTAG";
#else
		const int sufixSize = 32;
		if (userConnected && userManager != null)
		{
			SDK.XUserGetGamertag(userManager.CurrentUserData.userHandle, XUserGamertagComponent.Modern, out string gamertag);
			SDK.XUserGetGamertag(userManager.CurrentUserData.userHandle, XUserGamertagComponent.ModernSuffix, out string sufix);

			if (string.IsNullOrEmpty(sufix))
			{
				return gamertag;
			}

			return $"{gamertag}<size={sufixSize}>#{sufix}</size>";
		}

		return ""; 
#endif
	}

	bool IPlatform.HasUserConnected()
    {
        return userConnected;
    }
	bool IPlatform.HasInternetConnection()
	{
		XNetworkingConnectivityHint hint;
		Int32 hr = SDK.XNetworkingGetConnectivityHint(out hint);
		if (HR.SUCCEEDED(hr))
		{
			if (hint.ConnectivityLevel == XNetworkingConnectivityLevelHint.InternetAccess || hint.ConnectivityLevel == XNetworkingConnectivityLevelHint.ConstrainedInternetAccess)
				return true;
			else
				return false;
		}
		else
		{
			return false;
		}

	}
	#endregion

	#region Achievements
	void IPlatform.UnlockAchievement(int ID, Action<bool> callback)
    {
        int achievementID = ID;

        UnlockAchievement(achievementID.ToString(), callback);
    }
    void UnlockAchievement(string achievementID, Action<bool> callback)
    {
#if !UNITY_EDITOR
			SDK.XBL.XblAchievementsUpdateAchievementAsync(
				userManager.CurrentUserData.m_context,
				userManager.CurrentUserData.userXUID,
				achievementID,
				100,
				new SDK.XBL.XblAchievementsUpdateAchievementResult(x =>
				{
					callbackQueue.Enqueue(new BoolCallbackData(callback, x == 0));
				}));
#else
        callback?.Invoke(true);
#endif
    }
    public void SyncAchievements(Action<bool>/* Success */ callback)
    {
        if (!onlineServices)
        {
            callback?.Invoke(false);
            return;
        }

        SDK.XBL.XblAchievementsGetAchievementsForTitleIdAsync(
            userManager.CurrentUserData.m_context,
            userManager.CurrentUserData.userXUID,
            titleID,
            XblAchievementType.All,
            false,
            XblAchievementOrderBy.DefaultOrder,
            0,
            100,
            new SDK.XBL.XblAchievementsGetAchievementsForTitleIdResult((int hResult, XblAchievementsResultHandle handle) =>
            {
                if (hResult == 0)
                {
                    SDK.XBL.XblAchievementsResultGetAchievements(handle, out XblAchievement[] achievements);//Saida da lista de achievements
                    for (int i = 0; i < achievements.Length; i++)
                    {
                        XblAchievement achievement = achievements[i];
						/*Verificação com o estado atual do achievements*/
                        if (achievement.ProgressState != XblAchievementProgressState.Achieved )// Add the verification from the game save to check achievement unlock
                        {
                            UnlockAchievement(achievement.Id, null);
                        }
                    }
                }
            }));

        callback?.Invoke(true);
    }
	#endregion

	#region Presence
	void IPlatform.SetPresence(string id, params string[] extraInfo)
    {
#if !UNITY_EDITOR
        if (!userConnected || !onlineServices)
            return;


			XblPresenceRichPresenceIds.Create(scid, id, extraInfo, out XblPresenceRichPresenceIds presenceIds);

			SDK.XBL.XblPresenceSetPresenceAsync(
				userManager.CurrentUserData.m_context,
				true,
				presenceIds,
				new XblPresenceSetPresenceCompleted(x =>
				{
					Debug.Log("Xbox Platform" + $"Present set result: {x.ToString()}");
				})
				);
#else
        Debug.Log("Xbox Platform" + $"Settings current rich presence: {id}");
#endif
    }
    void IPlatform.ClearPresence()
    {
        DisablePresence();
    }
    void DisablePresence()
    {
        if (!onlineServices)
            return;

#if !UNITY_EDITOR
			const string defaultPresenceID = "MENU";
			XblPresenceRichPresenceIds.Create(scid, defaultPresenceID, new string[] { }, out XblPresenceRichPresenceIds presenceIds);

			SDK.XBL.XblPresenceSetPresenceAsync(
				userManager.CurrentUserData.m_context,
				false,
				presenceIds,
				new XblPresenceSetPresenceCompleted(x =>
				{
					Debug.Log("Xbox Platform" + $"Present set result: {x.ToString()}");
				})
				); 
#endif
    }
	#endregion

	#region Callbacks
	abstract class Callback
	{
		public abstract void Invoke();
	}
	class BoolCallbackData : Callback
	{
		Action<bool> callback;
		bool value;

		public BoolCallbackData(Action<bool> callback, bool value)
		{
			this.callback = callback;
			this.value = value;
		}

		public override void Invoke() => callback?.Invoke(value);
	}
	class SaveCallbackData : Callback
	{
		Action<bool, byte[], string> callback;
		bool value;
		byte[] data;
		string filename;

		public SaveCallbackData(Action<bool, byte[], string> callback, bool value, byte[] data, string filename)
		{
			this.callback = callback;
			this.value = value;
			this.data = data;
			this.filename = filename;
		}

		public override void Invoke()
		{
			callback?.Invoke(value, data, filename);
		}
	}

	private void GameCorePLM_OnApplicationSuspendingEvent(bool midLoad)
    {
        PlatformManager.instance.RequestGamePause();
        UnityEngine.WindowsGames.WindowsGamesPLM.AmReadyToSuspendNow();
    }
    private void GameCorePLM_OnResourceAvailabilityChangedEvent(bool amConstrained)
    {
        if (amConstrained)
            PlatformManager.instance.RequestGamePause();
    }
    private void DequeueCallbacks()
    {
        while (callbackQueue.Count > 0)
            callbackQueue.Dequeue().Invoke();
    }
	#endregion

}

#endif
