using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;


public class PlatformManager : MonoBehaviour
{
	public static PlatformManager instance { get; private set; }
	public IPlatform currentPlatform { get; private set; }

	public class PlatformEvent : EventArgs
	{
		public bool success;

		public string filename;

		public byte[] data;

		public PlatformEvent(bool success, string filename, byte[] data)
		{
			this.success = success;
			this.filename = filename;
			this.data = data;
		}
	}

	static public event EventHandler<PlatformEvent> OnGameLoadEnd;
	static public event EventHandler<PlatformEvent> OnGameSaveEnd;

	[SerializeField]
	public static int enterButtonVariable;
	int EnterButtonVariable { get => enterButtonVariable; }

	[SerializeField]
	public Texture2D textureSave;

	bool loadedData;


	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	static void Init()
	{
		const string prefabPath = "Porting/PlatformManager";

		if (instance)
			return;

		PlatformManager platformManagerPrefab = Resources.Load<PlatformManager>(prefabPath);
		if (platformManagerPrefab == null)
			throw new Exception($"[Platform Manager] Platform manager prefab not found at {prefabPath}");

		instance = Instantiate(platformManagerPrefab);
		instance.gameObject.SetActive(true);
		DontDestroyOnLoad(instance.gameObject);

		Application.backgroundLoadingPriority = ThreadPriority.High;

#if UNITY_SWITCH
			instance.currentPlatform = new SwitchPlatform();
#elif UNITY_GAMECORE
			instance.currentPlatform = new XboxPlatform();
#elif UNITY_PS4
			instance.currentPlatform = new PS4Platform();
#elif UNITY_PS5
            instance.currentPlatform = new PS5Platform();
#elif UNITY_STANDALONE
			instance.currentPlatform = new PCPlatform();
#endif

#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
		Debug.unityLogger.logEnabled = false;
#endif
	}

	#region Behaviours
	private void Start()
	{
		currentPlatform.OnPlatformStart();
	}
	private void Update()
	{
		currentPlatform.OnPlatformUpdate();

		if (!savingGame && savingList.Count > 0)
		{
			RequestGameSave(savingList[0], savingDataList[0]);
		}
	}
	#endregion

	#region Save/Load

	Coroutine gameSaveCoroutine;
	bool pedingSaving = false;
	List<string> savingList = new();
	List<byte[]> savingDataList = new();
	[HideInInspector] public bool savingGame;

	public void RequestGameSave(string filename, byte[] data)
	{
		RequestGameSave(true, filename, data);
	}
	public void RequestGameSave(bool manageDelay, string filename, byte[] data)
	{
		if (savingGame && manageDelay)
		{
			savingList.Add(filename);
			savingDataList.Add(data);
			pedingSaving = true;
			return;
		}
		savingGame = true;
		gameSaveCoroutine = StartCoroutine(ResquestGameSaveCoroutine(manageDelay, filename, data));
	}
	IEnumerator ResquestGameSaveCoroutine(bool manageDelay, string filename, byte[] data)
	{
		yield return 0f;
		if (!HasUserConnected())
		{
			savingGame = false;
			yield break;
		}
		currentPlatform.SaveGameData(data, filename, SaveDataCallback);
	}

	public void RequestGameLoad(string fileName)
	{
		currentPlatform.LoadGameData(fileName, LoadDataCallback);
	}

	void SaveDataCallback(bool success)
	{
		OnGameSaveEnd?.Invoke(this, new PlatformEvent(success, "", null));
		if (pedingSaving == true)
		{
			savingDataList.RemoveAt(0);
			savingList.RemoveAt(0);
			if (savingList.Count == 0) pedingSaving = false;
		}
		savingGame = false;
	}
	void LoadDataCallback(bool success, byte[] rawData, string filename)
	{
		OnGameLoadEnd?.Invoke(this, new PlatformEvent(success, filename, rawData));
	}
	#endregion

	#region User
	public int GetUserID() => currentPlatform.GetUserID();

	public string GetNetworkUserID() => currentPlatform.GetNetworkUserID();
	public string GetNickname() => currentPlatform.GetNickname();
	public string GetUniqueID() => currentPlatform.GetUniqueID();

	public bool HasUserConnected() => currentPlatform.HasUserConnected();
	public bool HasInternetConnection() => currentPlatform.HasInternetConnection();
	#endregion

	#region Achievements
	public void UnlockAchievement(int ID)
	{
		// All Platforms
		currentPlatform.UnlockAchievement(ID, null);
		Debug.LogError("Unlocking Achievement " + ID);
	}
	#endregion

	#region Presence
	public void SetPresence(string presenceID)
	{
		currentPlatform.SetPresence(presenceID);
	}
	#endregion

	#region Pause
	public void RequestGamePause()
	{
		//Call Pause Function
	}
	#endregion
}

