using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPlatform
{
	void OnPlatformStart();
	void OnPlatformUpdate();

	void SaveGameData(byte[] rawData, string filename, Action<bool /*success*/> callback);
	void LoadGameData(string fileName, Action<bool /*success*/, byte[] /*RawData*/, string/*filename*/> callback);

	int GetUserID();

	string GetNetworkUserID();
	string GetUniqueID();
	string GetNickname();

	bool HasUserConnected();
	bool HasInternetConnection();

	void UnlockAchievement(int ID, Action<bool /*success*/> callback);

	void SetPresence(string id, params string[] extraInfo);
	void ClearPresence();
}
