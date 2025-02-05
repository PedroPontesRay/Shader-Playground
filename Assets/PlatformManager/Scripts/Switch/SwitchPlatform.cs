using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
#if UNITY_SWITCH
using nn;
using nn.account;
using nn.fs;
using nn.hid;
using UnityEngine.Switch;
using sdsg.NintendoSwitch;
using nn.swkbd;
using nn.friends;

public class SwitchPlatform : IPlatform
{
    static string fileName = "playerData";
    static string mountName = "SaveData";
    static string filePath = mountName + ":/";
    const long fileOffset = 0;

    const float controllerSupportAppletShowDelay = 0.25f;
    float controllerSupportAppletShowCounter = 0.25f;

    nn.account.NetworkServiceAccountId networkUserID;
    nn.account.Uid userID;
    nn.account.UserHandle userHandle;
	nn.account.Nickname nickname;

	private static NetworkUseRequest networkUseRequest = null;

	bool haveNetworkAccount;
	bool isConnecting;

	string tokenString = "";

	nn.hid.GestureState[] gestureStates = new nn.hid.GestureState[nn.hid.Gesture.StateCountMax];

    bool userInitialized;

#region Behaviours
    void IPlatform.OnPlatformStart()
    {
#if !UNITY_EDITOR
			nn.account.Account.Initialize();
			nn.account.Account.TryOpenPreselectedUser(ref userHandle);
			nn.account.Account.GetUserId(ref userID, userHandle);
			userInitialized = true;

			nn.hid.Npad.Initialize();
			nn.hid.NpadJoy.SetHoldType(nn.hid.NpadJoyHoldType.Horizontal);
			nn.hid.Npad.SetSupportedIdType(new nn.hid.NpadId[] { nn.hid.NpadId.Handheld, nn.hid.NpadId.No1 });
			nn.hid.Npad.SetSupportedStyleSet(nn.hid.NpadStyle.Handheld | nn.hid.NpadStyle.FullKey | nn.hid.NpadStyle.JoyDual);

			nn.account.NetworkServiceAccount.IsAvailable(ref haveNetworkAccount, userHandle);

			if (haveNetworkAccount)
            {
				nn.account.NetworkServiceAccount.GetId(ref networkUserID, userHandle);
            }
			else
			{
				networkUserID.id = 0;
			}

            nn.account.Account.GetNickname(ref nickname, userID);

			nn.hid.Gesture.Initialize();

			networkUseRequest = new NetworkUseRequest();
#endif
	}
	void IPlatform.OnPlatformUpdate()
    {

    }
#endregion

#region Save/Load
    void IPlatform.SaveGameData(byte[] rawData, string fileName, Action<bool> callback)
    {
        SwitchSaveGame(rawData, fileName, callback);
    }
    void IPlatform.LoadGameData(string fileName, Action<bool, byte[], string> callback)
    {
        SwitchLoadGame(fileName, callback);
    }
    private void SwitchSaveGame(byte[] rawData, string filename, Action<bool> callback)
    {
#if !UNITY_EDITOR
			    fileName = filename;
				nn.Result result;
				SwitchMount();
				long saveSize = rawData.LongLength;
				SwitchCreateData(saveSize);

				nn.fs.FileHandle fileHandle = new nn.fs.FileHandle();
				SwitchOpen(ref fileHandle, nn.fs.OpenFileMode.Write);

				result = nn.fs.File.Write(fileHandle, fileOffset, rawData, saveSize, nn.fs.WriteOption.Flush);
				result.abortUnlessSuccess();

				nn.fs.File.Close(fileHandle);

				result = nn.fs.FileSystem.Commit(mountName);
				result.abortUnlessSuccess();

				SwitchUnmount();
				callback?.Invoke(true);
#else
        callback?.Invoke(true);
#endif
    }
    private void SwitchLoadGame(string filename, Action<bool, byte[], string> callback)
    {
        fileName = filename;
#if !UNITY_EDITOR
				nn.Result result;
				SwitchMount();
				if (!SwitchExistsData())
				{
					SwitchUnmount();
					callback.Invoke(true, new byte[] { },filename);
					return;
				}

				nn.fs.FileHandle fileHandle = new nn.fs.FileHandle();
				SwitchOpen(ref fileHandle, nn.fs.OpenFileMode.Read);

				long fileSize = 0;
				result = nn.fs.File.GetSize(ref fileSize, fileHandle);
				result.abortUnlessSuccess();

				byte[] saveDataRaw = new byte[fileSize];
				result = nn.fs.File.Read(fileHandle, fileOffset, saveDataRaw, fileSize);
				result.abortUnlessSuccess();

				nn.fs.File.Close(fileHandle);

				SwitchUnmount();
				callback.Invoke(true, saveDataRaw,filename);
#else
        callback?.Invoke(true, new byte[] { }, filename);
#endif
    }
    private void SwitchMount()
    {
        nn.Result result = nn.fs.SaveData.Mount(mountName, userID);
        result.abortUnlessSuccess();
    }
    private void SwitchUnmount()
    {
        nn.fs.FileSystem.Unmount(mountName);
    }
    private void SwitchCreateData(long fileSize)
    {
        nn.Result result;
        if (SwitchExistsData())
        {
            result = nn.fs.File.Delete(filePath + fileName);
            result.abortUnlessSuccess();
        }

        result = nn.fs.File.Create(filePath + fileName, fileSize);
        result.abortUnlessSuccess();
    }
    private void SwitchOpen(ref nn.fs.FileHandle fileHandle, nn.fs.OpenFileMode openFileMode)
    {
        nn.Result result = nn.fs.File.Open(ref fileHandle, filePath + fileName, openFileMode);
        result.abortUnlessSuccess();
    }
    private bool SwitchExistsData()
    {
        nn.fs.EntryType entryType = nn.fs.EntryType.File;
        nn.Result result = nn.fs.FileSystem.GetEntryType(ref entryType, filePath + fileName);
        bool exists = result.IsSuccess();

        return exists;
    }
#endregion

#region User
    int IPlatform.GetUserID()
    {
        return (int)userID._data0;
    }

    string IPlatform.GetNetworkUserID()
    {
        return networkUserID.id.ToString();
    }
	string IPlatform.GetUniqueID()
	{
		return tokenString;
	}
	string IPlatform.GetNickname()
	{
		return nickname.name;
	}

	bool IPlatform.HasUserConnected()
    {
        return userInitialized;
    }
	bool IPlatform.HasInternetConnection()
	{
		return NetworkManager.IsAvailable;
	}
#endregion

#region Achievement
	void IPlatform.UnlockAchievement(int ID, Action<bool> callback)
    {
        callback?.Invoke(true);
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
}
#endif
