#if UNITY_STANDALONE
#if !DISABLESTEAMWORKS
using Steamworks;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;


public class PCPlatform : IPlatform
{
	byte overlayActiveStatus = 0;
	private static System.Action m_completed;
	private static bool m_open = false;
	public static event Action<bool> blockInput;
#if !DISABLESTEAMWORKS
	Callback<GameOverlayActivated_t> gameOverlayActivatedCallback;
#endif

	#region Behaviours
	void IPlatform.OnPlatformStart()
	{
#if !DISABLESTEAMWORKS
		if (!SteamManager.Initialized)
		{
			Debug.Log("Steam Manager is not initialized");
			return;
		}
		
#endif
		Debug.Log("Platform Initialized");
		Application.focusChanged += Application_focusChanged;
#if !DISABLESTEAMWORKS
        gameOverlayActivatedCallback = Callback<GameOverlayActivated_t>.Create(OnGameOverlayChange);
		PlatformManager.instance.StartCoroutine(WaitingToCreateCallback());
#endif
	}
	void IPlatform.OnPlatformUpdate()
	{
		CheckOverlayStatus();
	}
	#endregion

	#region Save/Load
	void IPlatform.SaveGameData(byte[] rawData, string filename, Action<bool> callback)
	{
		File.WriteAllBytes(Application.persistentDataPath + "/" + filename + ".sav", rawData);
		callback?.Invoke(true);
	}
	void IPlatform.LoadGameData(string fileName, Action<bool, byte[], string> callback)
	{
		byte[] data = null;
		if (File.Exists(Application.persistentDataPath + "/" + fileName + ".sav"))
		{
			data = File.ReadAllBytes(Application.persistentDataPath + "/" + fileName + ".sav");
		}
		callback?.Invoke(true, data, fileName);
	}
	#endregion

	#region User
	int IPlatform.GetUserID()
	{
		return 0;
	}

	string IPlatform.GetNetworkUserID()
	{
#if !DISABLESTEAMWORKS
		return SteamUser.GetSteamID().m_SteamID.ToString();
#else
	    return "PadilhaTesteID";
#endif
	}
	string IPlatform.GetUniqueID()
	{
#if !DISABLESTEAMWORKS
		return SteamUser.GetSteamID().m_SteamID.ToString();
#else
		return "User";
#endif
	}
	string IPlatform.GetNickname()
	{
#if !DISABLESTEAMWORKS
		return SteamFriends.GetPersonaName();
#else
		return "User";
#endif
	}

	bool IPlatform.HasUserConnected()
	{
		return true;
	}
	bool IPlatform.HasInternetConnection()
	{
		if (Application.internetReachability == NetworkReachability.NotReachable)
		{
			return false;
		}
		else if (Application.internetReachability == NetworkReachability.ReachableViaCarrierDataNetwork)
		{
			return true;
		}
		else if (Application.internetReachability == NetworkReachability.ReachableViaLocalAreaNetwork)
		{
			return true;
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
		string achievement = "ACH_" + ID.ToString();
#if !DISABLESTEAMWORKS
		Steamworks.SteamUserStats.SetAchievement(achievement);
		Steamworks.SteamUserStats.StoreStats();
#endif
		callback?.Invoke(true);
	}
	#endregion

	#region Presence
	void IPlatform.SetPresence(string id, params string[] extraInfo)
	{
#if !DISABLESTEAMWORKS
		if (!SteamManager.Initialized)
			return;
#endif

		Debug.Log("PCPlatform" + $"Setting presence to: {id}");
#if !DISABLESTEAMWORKS
		Steamworks.SteamFriends.SetRichPresence("steam_display", $"#{id}");
#endif
	}
	void IPlatform.ClearPresence()
	{
#if !DISABLESTEAMWORKS
		if (!SteamManager.Initialized)
			return;
#endif

#if !DISABLESTEAMWORKS
		Steamworks.SteamFriends.SetRichPresence("steam_display", null);
#endif
	}
	#endregion

	#region Callback

#if !DISABLESTEAMWORKS
	private IEnumerator WaitingToCreateCallback()
	{
		yield return new WaitForSeconds(1.0f);

		while (!SteamManager.Initialized)
		{
			yield return null;
		}

		while (true)
		{
			yield return new WaitForSeconds(5.0f);

			SteamAPI.RunCallbacks();
		}
	}
#endif
	#endregion

	#region Overlay
	private void Application_focusChanged(bool onFocus)
	{
		if (!onFocus)
			PlatformManager.instance.RequestGamePause();
	}
#if !DISABLESTEAMWORKS
	void OnGameOverlayChange(GameOverlayActivated_t callback)
	{
		overlayActiveStatus = callback.m_bActive;	    
	}
#endif
	void CheckOverlayStatus()
	{
		switch (overlayActiveStatus)
		{
			case 0: // Closed

				break;
			case 1: // Open
				PlatformManager.instance.RequestGamePause();
				break;
		}
	}
	#endregion
}

#endif
