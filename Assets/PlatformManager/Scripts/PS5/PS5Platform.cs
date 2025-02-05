using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
#if UNITY_PS5
using static UnityEngine.PS5.PS5Input;
using Unity.PSN.PS5.WebApi;
using Unity.PSN.PS5.Aysnc;
using Unity.PSN.PS5.Users;
using Unity.PSN.PS5;
using Unity.PSN.PS5.Initialization;
using Unity.PSN.PS5.Commerce;
using Unity.PSN.PS5.Dialogs;
using Unity.PSN.PS5.Trophies;
using Unity.PSN.PS5.UDS;
using Unity.PSN.PS5.Auth;
using Unity.PSN.PS5.GameIntent;
using UnityEngine.PS5;
using Unity.PSN.PS5.Checks;

public class PS5Platform : IPlatform
{
    public const string fileName = "SaveData";

	PS5SaveDataTask saveDataTask;
	bool dataTaskInitialized;

	static bool userInitialized;
    bool udsInitialized;

    public static bool usedActivity = false;
    public static string ActivityName = "";
    bool joinningGame;

    static UnityEngine.PS5.PS5Input.LoggedInUser mainUser;
    private static UnityEngine.PS5.PS5Input.LoggedInUser playerInfo;

    List<Action> responseActions = new List<Action>();

	public bool networkConnected = false;

	#region Behaviours
	void IPlatform.OnPlatformStart()
    {
#if !UNITY_EDITOR
			saveDataTask = new PS5SaveDataTask();
            if (!userInitialized)
            {
                for (int i = 0; i < 4; ++i)
                {
                    var user = UnityEngine.PS5.PS5Input.GetUsersDetails(i);
                    Debug.LogError("AddingUser: " + user);

                    if (user.status != 0)
                    {
                        if (user.primaryUser)
                        {
                            mainUser = user;
							userInitialized = true;
							break;
                        }
                        Debug.Log("User " + i + " is logged");
                    }
                    else
                    {
                        Debug.Log("User " + i + " not logged in");
                    }
                }
            }
            saveDataTask.Init(mainUser.userId, PlatformManager.instance);
            dataTaskInitialized = true;
            try
            {
                InitResult initResult = Main.Initialize();


                // RequestCallback.OnRequestCompletion += OnCompleteion;

                if (initResult.Initialized == true)
                {
                    InitializeUniversalDataSystem();
                    StartTrophySystem();
					InitializeUser();
				}
            }
            catch (PSNException e)
            {
                //Debug.LogError("Exception During Initialization : " + e.ExtendedMessage);
            }

            Sony.PS5.Dialog.Main.Initialise();

			GameIntentSystem.OnGameIntentNotification += OnGameIntentNotification;
		    UserSystem.OnReachabilityNotification += OnReachabilityNotification;

			EnableReachabilityStateCallback();

#endif // UNITY_EDITOR


		Debug.Log("PS5 Platform: Platform start");
    }
    void IPlatform.OnPlatformUpdate()
    {
#if !UNITY_EDITOR
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
				PlatformManager.instance.RequestGameSave(true,filename,rawData);
				callback?.Invoke(false);
				return;
			}

			PlatformManager.instance.StartCoroutine(saveDataTask.CommitSaveTask(rawData,filename, (result) =>
			{
				callback.Invoke(result == SaveDataResult.Success);
			})); 
#else
		callback?.Invoke(true);
#endif
	}
	void IPlatform.LoadGameData(string filename, Action<bool, byte[], string> callback)
	{
#if !UNITY_EDITOR
			PlatformManager.instance.StartCoroutine(saveDataTask.LoadSaveTask(filename, (result, rawData, fileName) =>
			{
				switch (result)
				{
					case SaveDataResult.Success:
						callback?.Invoke(true, rawData, fileName);
						break;
					case SaveDataResult.DoesntExists:
						callback?.Invoke(true, new byte[] { }, fileName);
						break;
					default:
						callback?.Invoke(false, null, fileName);
						break;
				}

			})); 
#else
		callback?.Invoke(true, new byte[] { }, filename);
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
#endif
	}
	bool IPlatform.HasInternetConnection()
	{
		return networkConnected;
	}
	#endregion

	#region Achievements
	void IPlatform.UnlockAchievement(int ID, Action<bool> callback)
    {
#if !UNITY_EDITOR

			int trophyID = ID;

            UniversalDataSystem.UnlockTrophyRequest request = new UniversalDataSystem.UnlockTrophyRequest();

            request.TrophyId = trophyID;
            request.UserId = mainUser.userId;

            var getTrophyOp = new AsyncRequest<UniversalDataSystem.UnlockTrophyRequest>(request).ContinueWith((antecedent) =>
            {
                if (CheckAysncRequestOK(antecedent))
                {
                    Debug.LogError("Trophy Unlock Request finished = " + antecedent.Request.TrophyId);
                }
            });

            UniversalDataSystem.Schedule(getTrophyOp);

#endif // UNITY_EDITOR
    }
	#endregion

	#region Presence
	void IPlatform.SetPresence(string id, params string[] extraInfo)
    {

    }
    void IPlatform.ClearPresence()
    {

    }
	#endregion

	#region UDS
	public void InitializeUniversalDataSystem()
    {
        //allow trophy unlocking
        UniversalDataSystem.StartSystemRequest request = new UniversalDataSystem.StartSystemRequest();

        request.PoolSize = 256 * 1024;
        var requestOp = new AsyncRequest<UniversalDataSystem.StartSystemRequest>(request).ContinueWith((antecedent) =>
        {
            if (CheckAysncRequestOK(antecedent))
            {
                udsInitialized = true;
                Debug.LogError("Started UDS");
            }
        });

        UniversalDataSystem.Schedule(requestOp);
    }

    public void StartTrophySystem()
    {
        TrophySystem.StartSystemRequest request = new TrophySystem.StartSystemRequest();

        var requestOp = new AsyncRequest<TrophySystem.StartSystemRequest>(request).ContinueWith((antecedent) =>
        {
            if (CheckAysncRequestOK(antecedent))
            {
                Debug.LogError("STARTED TROPHY SYSTEM");
            }
        });

        TrophySystem.Schedule(requestOp);
    }
    public void InitializeUser()
    {
        UserSystem.AddUserRequest userRequest = new UserSystem.AddUserRequest() { UserId = mainUser.userId };

        var userRequestOp = new AsyncRequest<UserSystem.AddUserRequest>(userRequest).ContinueWith((antecedent) =>
        {
            if (antecedent != null && antecedent.Request != null)
            {
                if (CheckAysncRequestOK(antecedent))
                {
                    Debug.LogError("User Initalised");
                }
            }
        });

        UserSystem.Schedule(userRequestOp);
    }

    private void OnGameIntentNotification(GameIntentSystem.GameIntent gameIntent)
    {
        Debug.LogError("Player Session - GameIntent");
        if (gameIntent.UserId != mainUser.userId)
            return;

        if (gameIntent.IntentType == GameIntentSystem.GameIntent.IntentTypes.LaunchActivity)
        {
            Debug.LogError("Player Session - Game Intent - LaunchActivity");
            if (gameIntent is GameIntentSystem.LaunchActivity)
            {
                GameIntentSystem.LaunchActivity launchActivity = gameIntent as GameIntentSystem.LaunchActivity;
                Debug.LogError("Player Session - Game Intent - LaunchActivity");
                Debug.LogError("LaunchActivityID " + launchActivity.ActivityId);
                if (usedActivity)
                    return;

                if (joinningGame)
                    return;

                PlatformManager.instance.StartCoroutine(StartActivity(launchActivity.ActivityId));
                ActivityName = launchActivity.ActivityId;

            }
        }
    }

    private IEnumerator StartActivity(string activityID)
    {
        yield return new WaitForSeconds(1);
        usedActivity = true;
        joinningGame = true;
        StartUDSActivity("activityStart", activityID);

        yield return null;
    }

    public static IEnumerator EndActivity(string activityID)
    {
        StartUDSActivity("activityEnd", activityID);
        yield return null;
    }

    public static void StartUDSActivity(string activityName, string activityId)
    {
        UniversalDataSystem.UDSEvent myEvent = new UniversalDataSystem.UDSEvent();

        myEvent.Create(activityName);

        UniversalDataSystem.EventProperty prop = new UniversalDataSystem.EventProperty("activityId", activityId);

        myEvent.Properties.Set(prop);

        UniversalDataSystem.PostEventRequest request = new UniversalDataSystem.PostEventRequest();

        request.UserId = mainUser.userId;
        request.EventData = myEvent;

        var requestOp = new AsyncRequest<UniversalDataSystem.PostEventRequest>(request).ContinueWith((antecedent) =>
        {
            if (CheckAysncRequestOK(antecedent))
            {
                Debug.LogError("WORKED FINE: " + activityId + " : " + antecedent.Request.EventData.Name);
            }
            else
            {
                Debug.LogError("EVENT ERROR ACTIVITY ID");
            }
        });

        UniversalDataSystem.Schedule(requestOp);
    }
	private void EnableReachabilityStateCallback()
	{

		UserSystem.StartReachabilityStateCallbackRequest request = new UserSystem.StartReachabilityStateCallbackRequest();


		var requestOp = new AsyncRequest<UserSystem.StartReachabilityStateCallbackRequest>(request).ContinueWith((antecedent) =>
		{
			if (CheckAysncRequestOK(antecedent))
			{
				Debug.LogError("Signin reachability started");
			}
		});

		UserSystem.Schedule(requestOp);
	}
	private void OnReachabilityNotification(UserSystem.ReachabilityEvent reachabilityEvent)
	{
		if (reachabilityEvent == null) return;

		if (reachabilityEvent.State == UserSystem.ReachabilityStates.Unavailable)
		{
			networkConnected = false;
		}
		else if (reachabilityEvent.State == UserSystem.ReachabilityStates.Reachable)
		{
			networkConnected = true;
		}
	}
	#endregion

	#region Callbacks
	void UpdatePauseGame()
	{
		if (UnityEngine.PS5.Utility.isInBackgroundExecution || UnityEngine.PS5.Utility.isSystemUiOverlaid)
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
		try
		{
			Main.Update();
		}
		catch (Exception e)
		{
			Debug.LogError("Main.Update Exception : " + e.Message);
			Debug.LogError(e.StackTrace);
		}

		if (dataTaskInitialized && userInitialized)
		{
			saveDataTask.UpdateState();
		}

		Sony.PS5.Dialog.Main.Update();
	}
	void OnUserService(UnityEngine.PS5.PS5Input.UserServiceEventType eventtype, uint userID)
	{
		Debug.Log("PSPlatform" + $"User service event ID: {eventtype}, user: {userID}");
	}
	public static bool CheckAysncRequestOK<R>(AsyncRequest<R> asyncRequest) where R : Request
    {
        if (asyncRequest == null)
        {
            UnityEngine.Debug.LogError("AsyncRequest is null");
            return false;
        }

        return CheckRequestOK<R>(asyncRequest.Request);
    }
    public static bool CheckRequestOK<R>(R request) where R : Request
    {
        if (request == null)
        {
            Debug.LogError("Request is null");
            return false;
        }

        if (request.Result.apiResult == APIResultTypes.Success)
        {
            return true;
        }

        OutputApiResult(request.Result);

        return false;
    }

    public static void OutputApiResult(APIResult result)
    {
        if (result.apiResult == APIResultTypes.Success)
        {
            return;
        }

        string output = result.ErrorMessage() + " " + result.sceErrorCode + " " + result.apiResult;

        if (result.apiResult == APIResultTypes.Error)
        {
            Debug.LogError(output);
            //OnScreenLog.AddError(output);
        }
        else
        {
            Debug.LogError(output);
            //OnScreenLog.AddWarning(output);
        }
    }
	#endregion

}
#endif