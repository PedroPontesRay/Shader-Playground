using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Xml;
using UnityEngine.VFX;

#if UNITY_PS4
using Sony.NP;
public class PS4Platform : IPlatform
{
    public const string fileName = "SaveData";
    PS4SaveDataTask saveDataTask;
	bool dataTaskInitialized;

	static bool userInitialized;
    UnityEngine.PS4.PS4Input.LoggedInUser mainUser;
    static public int enterButtonParam { get; private set; } = 1;

    Sony.NP.Core.EmptyResponse trophyResponse;
    bool waitingTrophyResponse;

    List<Action> responseActions = new List<Action>();

    List<Sony.NP.NpCallbackEvent> m_npEventsToDispatch;

	bool networkconnected;

	#region Behaviours
	void IPlatform.OnPlatformStart()
    {
#if !UNITY_EDITOR
			UnityEngine.PS4.RenderSettings.SetNativeJobsSubmissionMode(UnityEngine.PS4.RenderSettings.NativeJobsSubmissionMode.Concatenated);
		    saveDataTask = new PS4SaveDataTask();

			m_npEventsToDispatch = new List<Sony.NP.NpCallbackEvent>(capacity: 8);

			for (int i = 0; i < 4; ++i)
			{
				var user = UnityEngine.PS4.PS4Input.GetUsersDetails(i);

				if (user.status != 0)
				{
					if (user.primaryUser)
					{
						mainUser = user;
						break;
					}
				}
				else
				{
					//Debug.Log("User " + i + " not logged in");
				}
			}

			Sony.NP.Main.OnAsyncEvent += OnPS4Event_NpToolkitCallback;

			Sony.NP.InitToolkit initParams = new Sony.NP.InitToolkit();
			initParams.contentRestrictions.ApplyContentRestriction = true;
#if EU_BUILD
        initParams.contentRestrictions.DefaultAgeRestriction = PS4AgeRestrictionHelper.defaultEU;

        AgeRestriction[] ageRestrictions = PS4AgeRestrictionHelper.GetAgeRestrictionsEU();

        initParams.contentRestrictions.AgeRestrictions = ageRestrictions;
#else
		initParams.contentRestrictions.DefaultAgeRestriction = PS4AgeRestrictionHelper.defaultNA;

		AgeRestriction[] ageRestrictions = PS4AgeRestrictionHelper.GetAgeRestrictionsNA();

		initParams.contentRestrictions.AgeRestrictions = ageRestrictions;
#endif
		initParams.SetPushNotificationsFlags(Sony.NP.PushNotificationsFlags.None);

			Sony.NP.InitResult initResult = Sony.NP.Main.Initialize(initParams);
			try
			{
				Sony.NP.Trophies.RegisterTrophyPackRequest packRequest = new Sony.NP.Trophies.RegisterTrophyPackRequest();
				packRequest.UserId = mainUser.userId;
				Sony.NP.Trophies.RegisterTrophyPack(packRequest, new Sony.NP.Core.EmptyResponse());
			}
			catch (Exception e)
			{
				//Debug.LogError($"[PS Platform] Register trophy pack error: {e.Message}");
				return;
			}

			Sony.PS4.Dialog.Main.Initialise();

			saveDataTask.Init(mainUser.userId, PlatformManager.instance);
			dataTaskInitialized = true;
			userInitialized = true;

			enterButtonParam = UnityEngine.PS4.Utility.GetSystemServiceParam(UnityEngine.PS4.Utility.SystemServiceParamId.EnterButtonAssign);

			UnityEngine.PS4.PS4Input.OnUserServiceEvent += OnUserService;

		    GetDetailedNetworkInfo();
#endif // UNITY_EDITOR

		PlatformManager.enterButtonVariable = enterButtonParam;
        Debug.Log("PS Platform: Platform start");
    }
    void IPlatform.OnPlatformUpdate()
    {
#if !UNITY_EDITOR
			if (m_npEventsToDispatch.Count > 0)
			{
				var callback = m_npEventsToDispatch[0];
				m_npEventsToDispatch.RemoveAt(0);
				OnPS4Event_NpToolkitEventDispatcher(callback);
			}

			UpdatePauseGame();
			UpdateResponses();
			UpdateServices();
#endif 
    }
	#endregion

	#region Save/Load
	void IPlatform.SaveGameData(byte[] rawData, string filename, Action<bool> callback)
	{
#if !UNITY_EDITOR
			if (saveDataTask.IsBusy)
			{
				PlatformManager.instance.RequestGameSave(true, filename, rawData);
				callback?.Invoke(false);
				return;
			}

			PlatformManager.instance.StartCoroutine(saveDataTask.CommitSaveTask(rawData, filename, (result) =>
			{
				callback.Invoke(result == SaveDataResult.OK);
			})); 
#else
		callback?.Invoke(true);
#endif
	}
	void IPlatform.LoadGameData(string fileName, Action<bool, byte[], string> callback)
	{
#if !UNITY_EDITOR
			PlatformManager.instance.StartCoroutine(saveDataTask.LoadSaveTask(fileName,(result, rawData, filename) =>
			{
				switch (result)
				{
					case LoadDataResult.OK:
						callback?.Invoke(true, rawData,filename);
						break;
					case LoadDataResult.NULL:
						callback?.Invoke(true, new byte[] { }, filename);
						break;
					default:
						callback?.Invoke(false, null, filename);
						break;
				}

			})); 
#else
		callback?.Invoke(true, new byte[] { }, fileName);
#endif
	}
	#endregion

	#region User
	int IPlatform.GetUserID()
	{
		return mainUser.userId;
	}

	string IPlatform.GetNetworkUserID()
	{
		return mainUser.accountId.ToString();
	}
	string IPlatform.GetUniqueID()
	{
		return mainUser.accountId.ToString();
	}
	string IPlatform.GetNickname()
	{
		return mainUser.userName;
	}

	bool IPlatform.HasUserConnected()
	{
#if !UNITY_EDITOR
			return userInitialized; 
#else
		return false;
#endif // UNITY_EDITOR
	}
	bool IPlatform.HasInternetConnection()
	{
		return networkconnected;
	}
	#endregion

	#region Achievements
	void IPlatform.UnlockAchievement(int ID, Action<bool> callback)
    {
#if !UNITY_EDITOR
			int trophyID = ID;

			Sony.NP.Trophies.UnlockTrophyRequest trophyRequest = new Sony.NP.Trophies.UnlockTrophyRequest
			{
				TrophyId = trophyID,
				UserId = mainUser.userId,
				Async = true
			};

			waitingTrophyResponse = true;
			trophyResponse = new Sony.NP.Core.EmptyResponse();

			Sony.NP.Trophies.UnlockTrophy(trophyRequest, trophyResponse);         
#endif
    }
	#endregion

	#region Presence
	void IPlatform.SetPresence(string id, params string[] extraInfo)
    {
        Debug.Log("PSPlatform" + $"Setting user presence: {id}");

        id = $"Presence_{id}";

        if (!userInitialized)
        {
            Debug.Log("PSPlatform" + "User not initialized");
            return;
        }
#if !UNITY_EDITOR
			Sony.NP.Presence.SetPresenceRequest request = new Sony.NP.Presence.SetPresenceRequest();
			request.UserId = mainUser.userId;
			var localizedPresence = PSLocalizedPresence.GetPresence(id);
			request.LocalizedGameStatuses = localizedPresence;
			request.DefaultGameStatus = localizedPresence[0].GameStatus; // 0 is english localization
			Sony.NP.Presence.SetPresence(request, new Sony.NP.Core.EmptyResponse()); 
#endif
    }
    void IPlatform.ClearPresence()
    {

    }
	#endregion

	#region Callbacks
	void UpdatePauseGame()
	{
		if (UnityEngine.PS4.Utility.isInBackgroundExecution || UnityEngine.PS4.Utility.isSystemUiOverlaid)
			PlatformManager.instance.RequestGamePause();
	}

	void UpdateResponses()
	{
		for (int i = 0; i < responseActions.Count; i++)
		{
			responseActions[i]?.Invoke();
		}

		responseActions.Clear();
	}
	void AddResponse(Action response) => responseActions.Add(response);
	void UpdateServices()
	{
		if (dataTaskInitialized && userInitialized)
		{
			saveDataTask.UpdateState();
		}

		Sony.NP.Main.Update();
		Sony.PS4.Dialog.Main.Update();
	}

	void OnPS4Event_NpToolkitCallback(Sony.NP.NpCallbackEvent callbackEvent)
    {
        // Enquee to dispatch at the Main Thread
        m_npEventsToDispatch.Add(callbackEvent);
    }
    void OnPS4Event_NpToolkitEventDispatcher(Sony.NP.NpCallbackEvent callbackEvent)
    {
        //Debug.Log("[PS4 NpToolkitEventDispatcher] Service(" + callbackEvent.Service + ") ApiCalled(" + callbackEvent.ApiCalled + ")");

        try
        {
            // Dispatch event
            switch (callbackEvent.Service)
            {
                case Sony.NP.ServiceTypes.Trophy:
                    break;
			}
			switch (callbackEvent.ApiCalled)
            {
				case FunctionTypes.NotificationNetStateChange:
					Sony.NP.NetworkUtils.NetStateChangeResponse networkCallback = callbackEvent.Response as Sony.NP.NetworkUtils.NetStateChangeResponse;
					OutputNetStateChange(networkCallback);
					break;
				case FunctionTypes.NetworkUtilsGetDetailedNetworkInfo:
					Sony.NP.NetworkUtils.DetailedNetworkInfoResponse networkDetailedCallback = callbackEvent.Response as Sony.NP.NetworkUtils.DetailedNetworkInfoResponse;
					GetDetailedNetworkInfoCallback(networkDetailedCallback);
					break;
			}
        }
        catch (Sony.NP.NpToolkitException e)
        {

        }
        catch (System.Exception e)
        {

        }
    }

	void GetDetailedNetworkInfo()
	{
		Sony.NP.NetworkUtils.GetDetailedNetworkInfoRequest request = new Sony.NP.NetworkUtils.GetDetailedNetworkInfoRequest()
		{
			Async = true,
			UserId = mainUser.userId,
		};

		Sony.NP.NetworkUtils.DetailedNetworkInfoResponse response = new Sony.NP.NetworkUtils.DetailedNetworkInfoResponse();

		int requestID = Sony.NP.NetworkUtils.GetDetailedNetworkInfo(request, response);
		Debug.LogError("[PS4] DetailedNetworkInfo : Request Id = " + requestID);
	}
	void GetDetailedNetworkInfoCallback(Sony.NP.NetworkUtils.DetailedNetworkInfoResponse response)
	{
		if(response.Link == NetworkUtils.NetworkLink.Connected)
		{
			networkconnected = true;
		}
		else networkconnected = false;
	}
	void OutputNetStateChange(NetworkUtils.NetStateChangeResponse networkCallback)
	{
		if (networkCallback == null) return;

		if (networkCallback.Locked == false)
		{
			bool connectionState = false;
			if (networkCallback.NetEvent == Sony.NP.NetworkUtils.NetworkEvent.networkConnected)
			{
				connectionState = true;
				networkconnected = true;
			}
			else if (networkCallback.NetEvent == Sony.NP.NetworkUtils.NetworkEvent.networkDisconnected)
			{
				connectionState = false;
				networkconnected = false;
			}
			Debug.LogError("** NETWORK STATE CHANGE EVENT DETECTED, IS CONNECTED? " + connectionState);
		}
	}
	void OnUserService(uint id, uint userID)
    {
        Debug.Log("PSPlatform" + $"User service event ID: {id}, user: {userID}");
    }
	#endregion
}
#endif