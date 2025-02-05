using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_PS5
using Unity.SaveData.PS5.Info;
using Unity.SaveData.PS5.Mount;
using Unity.SaveData.PS5.Core;

public enum SaveDataResult
{
	Success,
	DoesntExists,
	GenericError
}

class PS5FileWriteOperationRequest : Unity.SaveData.PS5.Info.FileOps.FileOperationRequest
{
	public byte[] Data;
	public string fileName;

	public override void DoFileOperations(Unity.SaveData.PS5.Mount.Mounting.MountPoint mp, Unity.SaveData.PS5.Info.FileOps.FileOperationResponse response)
	{
		string filePath = string.Format("{0}/{1}", mp.PathName.Data, fileName);

		int totalWritten = 0;

		Debug.Log("[SaveDataTask] Creating save file with size: " + Data.LongLength);

		using (FileStream fs = File.Open(filePath, FileMode.Create))
		{
			while (totalWritten < Data.Length)
			{
				int writeSize = Mathf.Min(Data.Length - totalWritten, 1000); // Write up to 1000 bytes

				fs.Write(Data, totalWritten, writeSize);

				totalWritten += writeSize;

				// Update progress value during saving
				response.UpdateProgress((float)totalWritten / (float)Data.Length);
			}
		}

		Debug.Log("[SaveDataTask] Total written: " + totalWritten);
	}
}
class PS5FileReadOperationRequest : Unity.SaveData.PS5.Info.FileOps.FileOperationRequest
{
	public string fileName;
	public override void DoFileOperations(Unity.SaveData.PS5.Mount.Mounting.MountPoint mp, Unity.SaveData.PS5.Info.FileOps.FileOperationResponse response)
	{
		var readResponse = response as PS5FileReadOperationResponse;

		string filePath = string.Format("{0}/{1}", mp.PathName.Data, fileName);

		FileInfo info = new FileInfo(filePath);

		readResponse.Data = new byte[info.Length];

		int totalRead = 0;

		using (FileStream fs = File.OpenRead(filePath))
		{
			while (totalRead < info.Length)
			{
				int readSize = Mathf.Min((int)info.Length - totalRead, 1000); // read up to 1000 bytes

				fs.Read(readResponse.Data, totalRead, readSize);

				totalRead += readSize;

				// Update progress value during loading
				response.UpdateProgress((float)totalRead / (float)info.Length);
			}
		}

		Debug.Log("[SaveDataTask] Total read: " + totalRead);
	}
}
class PS5FileWriteOperationResponse : Unity.SaveData.PS5.Info.FileOps.FileOperationResponse
{

}
class PS5FileReadOperationResponse : Unity.SaveData.PS5.Info.FileOps.FileOperationResponse
{
	public byte[] Data;
}

public class PS5SaveDataTask
{
	const string kMountDirName = "saves";
	//This supports 64mb saves
	const ulong kTotalBlocksSize = Unity.SaveData.PS5.Mount.Mounting.MountRequest.BLOCKS_MIN + ((1024 * 2 * 1024) / Unity.SaveData.PS5.Mount.Mounting.MountRequest.BLOCK_SIZE);

	int m_userID;
	MonoBehaviour m_routineDispatcher;
	Unity.SaveData.PS5.Initialization.InitResult m_initResult;


	List<Unity.SaveData.PS5.Core.SaveDataCallbackEvent> m_saveDataEventsToDispatch = new List<Unity.SaveData.PS5.Core.SaveDataCallbackEvent>(4);
	bool m_waitingForResponse;
	Unity.SaveData.PS5.Core.ReturnCodes m_sce_result;

	ulong m_requiredFreeBlocks;

	public bool IsBusy { get; private set; }

	public void Init(int userID, MonoBehaviour aRoutineDispatcher)
	{
		m_userID = userID;
		m_routineDispatcher = aRoutineDispatcher;

		Unity.SaveData.PS5.Main.OnAsyncEvent += OnPS5Event_SaveDataAsyncEvent;
		try
		{
			Unity.SaveData.PS5.Initialization.InitSettings settings = new Unity.SaveData.PS5.Initialization.InitSettings();

			settings.Affinity = Unity.SaveData.PS5.Initialization.ThreadAffinity.Core5;



			m_initResult = Unity.SaveData.PS5.Main.Initialize(settings);

			if (!m_initResult.Initialized == true)
			{
				Debug.LogError("[SaveDataTask] not initialized ");
			}
		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			Debug.LogError("Exception During Initialization : " + e.ExtendedMessage);
		}
	}
	public void Finish()
	{
		//Sony.PS4.SaveData.Main.Terminate();

		m_initResult = new Unity.SaveData.PS5.Initialization.InitResult();
	}

	public IEnumerator CommitSaveTask(byte[] data, string filename, System.Action<SaveDataResult> resultCallback)
	{
		var opResult = SaveDataResult.Success;

		IsBusy = true;

		yield return null;

		m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_BUSY;
		m_waitingForResponse = true;
		Mount(readWrite: true);
		while (m_waitingForResponse)
		{
			yield return null;
		}
		Debug.LogError(m_waitingForResponse.ToString());
		// check Mount async result
		switch (m_sce_result)
		{
			case Unity.SaveData.PS5.Core.ReturnCodes.SUCCESS:
				break;

			case Unity.SaveData.PS5.Core.ReturnCodes.DATA_ERROR_NO_SPACE_FS:
				{
					m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_BUSY;
					m_waitingForResponse = true;
					DisplayNoFreeSpaceDialog(m_requiredFreeBlocks);
					while (m_waitingForResponse)
					{
						yield return null;
					}

					resultCallback?.Invoke(SaveDataResult.GenericError);
					IsBusy = false;
					yield break;
				}

			case Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_BROKEN:
				{
					m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_BUSY;
					m_waitingForResponse = true;
					DisplayBrokenDataDeletedDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Save, Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageType.Corrupted);



					while (m_waitingForResponse)
					{
						yield return null;
					}

					m_waitingForResponse = true;
					Delete();
					while (m_waitingForResponse)
					{
						yield return null;
					}

					// check delete async result
					if (m_sce_result != Unity.SaveData.PS5.Core.ReturnCodes.SUCCESS)
					{
						m_waitingForResponse = true;
						DisplayErrorDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Save, unchecked((int)m_sce_result));
						while (m_waitingForResponse)
						{
							yield return null;
						}

						resultCallback?.Invoke(SaveDataResult.GenericError);
						IsBusy = false;
						yield break;
					}

					// redo process with clean save area
					IsBusy = false;
					m_routineDispatcher.StartCoroutine(CommitSaveTask(data, filename, resultCallback));
					yield break;
				}


			default:
				{
					m_waitingForResponse = true;
					DisplayErrorDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Save, unchecked((int)m_sce_result));
					while (m_waitingForResponse)
					{
						yield return null;
					}

					resultCallback?.Invoke(SaveDataResult.GenericError);
					IsBusy = false;
					yield break;
				}

		}

		Debug.LogError("ActiveMountPoints");
		// Acquire the active Mount point
		var mp = Unity.SaveData.PS5.Mount.Mounting.ActiveMountPoints[0];
		Debug.LogError("ActiveMountPointsFREE");
		m_waitingForResponse = true;
		SetMountParams(mp);

		while (m_waitingForResponse)
		{
			yield return null;
		}

		//Descommented because ps5 player settings icon not working 

		m_waitingForResponse = true;

		SaveIconFromFile(mp);

		while (m_waitingForResponse)
		{
			yield return null;
		}

		Debug.LogError("WRITE DATA");
		m_waitingForResponse = true;
		WriteData(data, filename, mp);

		while (m_waitingForResponse)
		{
			yield return null;
		}

		Debug.LogError("SAVING ASYNC");

		// check WriteData async result
		switch (m_sce_result)
		{
			case Unity.SaveData.PS5.Core.ReturnCodes.SUCCESS:
				break;

			default:
				{
					m_waitingForResponse = true;
					DisplayErrorDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Save, unchecked((int)m_sce_result));
					while (m_waitingForResponse)
					{
						yield return null;
					}

					opResult = SaveDataResult.GenericError;
				}
				break;
		}

		m_waitingForResponse = true;
		Unmount(mp, backup: true);
		while (m_waitingForResponse)
		{
			yield return null;
		}

		// check Unmount async result
		if (m_sce_result != Unity.SaveData.PS5.Core.ReturnCodes.SUCCESS)
		{
			m_waitingForResponse = true;
			DisplayErrorDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Save, unchecked((int)m_sce_result));
			while (m_waitingForResponse)
			{
				yield return null;
			}

			resultCallback?.Invoke(SaveDataResult.GenericError);
			IsBusy = false;
			yield break;
		}
		m_waitingForResponse = true;
		/*
		Backup();

		// wait for backup async result

		while (m_waitingForResponse)
		{
			yield return null;
		}

		// check backup async result
		if (m_sce_result != Unity.SaveData.PS5.Core.ReturnCodes.SUCCESS)
		{
			m_waitingForResponse = true;
			DisplayErrorDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Save, unchecked((int)m_sce_result));
			while (m_waitingForResponse)
			{
				yield return null;
			}
		}*/
		//if restore backup need to do Notification Backup verification



		// done
		Debug.LogError("ERROR DESC: " + m_sce_result.ToString());
		resultCallback?.Invoke(opResult);
		IsBusy = false;
	}
	public IEnumerator LoadSaveTask(string filename, System.Action<SaveDataResult, byte[], string> resultCallback)
	{
		var opResult = SaveDataResult.Success;

		yield return null;

		m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_BUSY;
		m_waitingForResponse = true;
		Mount(readWrite: false);
		while (m_waitingForResponse)
		{
			yield return null;
		}

		// check Mount async result
		switch (m_sce_result)
		{
			case Unity.SaveData.PS5.Core.ReturnCodes.SUCCESS:
				break;

			case Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_NOT_FOUND:
				{
					resultCallback?.Invoke(SaveDataResult.DoesntExists, null, filename);
					yield break;
				}


			case Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_BROKEN:
				{
					m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_BUSY;

					m_waitingForResponse = true;

					while (m_waitingForResponse)
					{
						yield return null;
					}

					// CheckBackup async result
					switch (m_sce_result)
					{
						case Unity.SaveData.PS5.Core.ReturnCodes.SUCCESS:
							{
								m_waitingForResponse = true;
								DisplayBrokenDataRestoreDialog();
								while (m_waitingForResponse)
								{
									yield return null;
								}

								//m_waitingForResponse = true;
								//while (m_waitingForResponse)
								//{
								//    yield return null;
								//}

								// check restore backup async result
								if (m_sce_result == Unity.SaveData.PS5.Core.ReturnCodes.DATA_ERROR_NO_SPACE_FS)
								{
									m_waitingForResponse = true;
									DisplayNoFreeSpaceRestoreDialog();
									while (m_waitingForResponse)
									{
										yield return null;
									}

									resultCallback?.Invoke(SaveDataResult.GenericError, null, filename);
									yield break;
								}

								else if (m_sce_result != Unity.SaveData.PS5.Core.ReturnCodes.SUCCESS)
								{
									m_waitingForResponse = true;
									DisplayErrorDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Save, unchecked((int)m_sce_result));
									while (m_waitingForResponse)
									{
										yield return null;
									}

									resultCallback?.Invoke(SaveDataResult.DoesntExists, null, filename);
									yield break;
								}

								// redo process with clean restored area
								m_routineDispatcher.StartCoroutine(LoadSaveTask(filename, resultCallback));
								yield break;

							}


						case Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_NOT_FOUND:
							{
								m_waitingForResponse = true;
								DisplayBrokenDataDeletedDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Load, Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageType.NoData);
								while (m_waitingForResponse)
								{
									yield return null;
								}

								m_waitingForResponse = true;
								Delete();
								while (m_waitingForResponse)
								{
									yield return null;
								}

								// check delete async result
								if (m_sce_result != Unity.SaveData.PS5.Core.ReturnCodes.SUCCESS)
								{
									m_waitingForResponse = true;
									DisplayErrorDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Save, unchecked((int)m_sce_result));
									while (m_waitingForResponse)
									{
										yield return null;
									}

									resultCallback?.Invoke(SaveDataResult.DoesntExists, null, filename);
									yield break;
								}

								// finishes as SAVE_DATA_ERROR_NOT_FOUND
								resultCallback?.Invoke(SaveDataResult.DoesntExists, null, filename);
								yield break;
							}


						default:
							m_waitingForResponse = true;
							DisplayErrorDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Load, unchecked((int)m_sce_result));
							while (m_waitingForResponse)
							{
								yield return null;
							}

							resultCallback?.Invoke(SaveDataResult.DoesntExists, null, filename);
							yield break;

					}
				}


			default:
				{
					m_waitingForResponse = true;
					DisplayErrorDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Load, unchecked((int)m_sce_result));
					while (m_waitingForResponse)
					{
						yield return null;
					}

					resultCallback?.Invoke(SaveDataResult.DoesntExists, null, filename);
					yield break;
				}

		}

		// Acquire the active Mount point
		var mp = Unity.SaveData.PS5.Mount.Mounting.ActiveMountPoints[0];

		byte[] data = null;

		m_waitingForResponse = true;
		var readResponse = ReadData(mp, filename);
		while (m_waitingForResponse)
		{
			yield return null;
		}

		if (readResponse != null)
		{
			// check ReadData async result
			switch (m_sce_result)
			{
				case Unity.SaveData.PS5.Core.ReturnCodes.SUCCESS:
					{
						data = readResponse.Data;
					}
					break;

				default:
					{
						m_waitingForResponse = true;
						DisplayErrorDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Save, unchecked((int)m_sce_result));
						while (m_waitingForResponse)
						{
							yield return null;
						}

						opResult = SaveDataResult.DoesntExists;
					}
					break;
			}
		}

		m_waitingForResponse = true;
		Unmount(mp, backup: true);

		while (m_waitingForResponse)
		{
			yield return null;
		}

		// check Unmount async result
		if (m_sce_result != Unity.SaveData.PS5.Core.ReturnCodes.SUCCESS)
		{
			m_waitingForResponse = true;
			DisplayErrorDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Load, unchecked((int)m_sce_result));
			while (m_waitingForResponse)
			{
				yield return null;
			}

			resultCallback?.Invoke(SaveDataResult.DoesntExists, null, filename);
			yield break;
		}

		// done
		resultCallback?.Invoke(opResult, data, filename);
	}

	void Mount(bool readWrite)
	{
		try
		{
			Unity.SaveData.PS5.Mount.Mounting.MountRequest request = new Unity.SaveData.PS5.Mount.Mounting.MountRequest();

			Unity.SaveData.PS5.Core.DirName dirName = new Unity.SaveData.PS5.Core.DirName();
			dirName.Data = kMountDirName;

			request.UserId = m_userID;
			request.Async = true;
			request.DirName = dirName;

			if (readWrite == true)
			{
				request.MountMode = Unity.SaveData.PS5.Mount.Mounting.MountModeFlags.Create2 |
									 Unity.SaveData.PS5.Mount.Mounting.MountModeFlags.CopyIcon |
									 Unity.SaveData.PS5.Mount.Mounting.MountModeFlags.ReadWrite;

				request.Blocks = kTotalBlocksSize;
			}
			else
			{
				request.MountMode = Unity.SaveData.PS5.Mount.Mounting.MountModeFlags.ReadOnly;
			}

			Unity.SaveData.PS5.Mount.Mounting.MountResponse response = new Unity.SaveData.PS5.Mount.Mounting.MountResponse();

			int requestID = Unity.SaveData.PS5.Mount.Mounting.Mount(request, response);
			Debug.Log("[SaveDataTask] Requested Mount : " + requestID);
		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			Debug.LogError("[SaveDataTask] Mount Exception : " + e.ExtendedMessage);
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_NOT_MOUNTED;
		}
	}
	void SetMountParams(Mounting.MountPoint mp)
	{
		try
		{
			Mounting.SetMountParamsRequest request = new Mounting.SetMountParamsRequest();

			if (mp == null) return;

			request.UserId = m_userID;
			request.MountPointName = mp.PathName;

			SaveDataParams sdParams = new SaveDataParams();

			sdParams.Title = "MetroLand Save";
			sdParams.SubTitle = "";
			sdParams.Detail = "";

			request.Params = sdParams;

			EmptyResponse response = new EmptyResponse();

			int requestId = Mounting.SetMountParams(request, response);
			Debug.LogError("PARAMETROS SETADOS");
		}
		catch (SaveDataException e)
		{
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
			Debug.LogError("Exception : " + e.ExtendedMessage);
		}
	}
	public void SaveIconFromFile(Mounting.MountPoint mp)
	{
		try
		{
			Mounting.SaveIconRequest request = new Mounting.SaveIconRequest();

			if (mp == null) return;

			request.UserId = m_userID;
			request.MountPointName = mp.PathName;

			byte[] bytesImage = PlatformManager.instance.textureSave.EncodeToPNG();

			request.RawPNG = bytesImage;

			EmptyResponse response = new EmptyResponse();

			int requestId = Mounting.SaveIcon(request, response);

		}
		catch
		{
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}
	}
	public void LoadIcon(Mounting.MountPoint mp)
	{
		try
		{
			Mounting.LoadIconRequest request = new Mounting.LoadIconRequest();

			if (mp == null) return;

			request.UserId = PlatformManager.instance.GetUserID();
			request.MountPointName = mp.PathName;

			Mounting.LoadIconResponse response = new Mounting.LoadIconResponse();

			int requestId = Mounting.LoadIcon(request, response);

			Debug.LogError("LoadIcon Async : Request Id = " + requestId);
		}
		catch
		{
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}
	}
	void Unmount(Unity.SaveData.PS5.Mount.Mounting.MountPoint mp, bool backup)
	{
		try
		{
			Unity.SaveData.PS5.Mount.Mounting.UnmountRequest request = new Unity.SaveData.PS5.Mount.Mounting.UnmountRequest();



			if (mp == null) return;

			request.UserId = m_userID;
			request.MountPointName = mp.PathName;
			//request.Backup = backup;

			Unity.SaveData.PS5.Core.EmptyResponse response = new Unity.SaveData.PS5.Core.EmptyResponse();

			int requestID = Unity.SaveData.PS5.Mount.Mounting.Unmount(request, response);
			Debug.Log("[SaveDataTask] Requested Unmount : " + requestID);
		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			Debug.LogError("[SaveDataTask] Unmount Exception : " + e.ExtendedMessage);
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}
	}

	public void Backup()
	{
		try
		{
			Unity.SaveData.PS5.Backup.Backups.BackupRequest request = new Unity.SaveData.PS5.Backup.Backups.BackupRequest();

			Unity.SaveData.PS5.Core.DirName dirName = new Unity.SaveData.PS5.Core.DirName();
			//Unity.SaveData.PS5.Core.FunctionTypes.Backup
			dirName.Data = kMountDirName;

			request.UserId = m_userID;
			request.DirName = dirName;

			Unity.SaveData.PS5.Core.EmptyResponse response = new Unity.SaveData.PS5.Core.EmptyResponse();

			int requestId = Unity.SaveData.PS5.Backup.Backups.Backup(request, response);

			//OnScreenLog.Add("Backup Async : Request Id = " + requestId);
		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
			Debug.LogError("Exception : " + e.ExtendedMessage);
		}

	}

	void WriteData(byte[] saveData, string filename, Unity.SaveData.PS5.Mount.Mounting.MountPoint mp)
	{
		try
		{

			PS5FileWriteOperationRequest request = new PS5FileWriteOperationRequest();

			request.UserId = m_userID;
			request.MountPointName = mp.PathName;
			request.Data = saveData;
			request.fileName = filename;


			PS5FileWriteOperationResponse response = new PS5FileWriteOperationResponse();

			int requestID = Unity.SaveData.PS5.Info.FileOps.CustomFileOp(request, response);
			Debug.LogError("[SaveDataTask] Requested CustomFileOp Write : " + requestID);
		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			Debug.LogError("[SaveDataTask] WriteData Exception : " + e.ExtendedMessage);
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}
		catch (System.Exception e)
		{
			Debug.LogException(e);
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}
	}
	PS5FileReadOperationResponse ReadData(Unity.SaveData.PS5.Mount.Mounting.MountPoint mp, string filename)
	{
		try
		{
			PS5FileReadOperationRequest request = new PS5FileReadOperationRequest();

			request.UserId = m_userID;
			request.MountPointName = mp.PathName;
			request.fileName = filename;

			PS5FileReadOperationResponse response = new PS5FileReadOperationResponse();

			int requestID = Unity.SaveData.PS5.Info.FileOps.CustomFileOp(request, response);
			Debug.Log("[SaveDataTask] Requested CustomFileOp Read : " + requestID);

			return response;
		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			Debug.LogError("[SaveDataTask] ReadData Exception : " + e.ExtendedMessage);
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}
		catch (System.Exception e)
		{
			Debug.LogException(e);
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}

		return null;
	}
	void Delete()
	{
		try
		{
			Unity.SaveData.PS5.Delete.Deleting.DeleteRequest request = new Unity.SaveData.PS5.Delete.Deleting.DeleteRequest();

			Unity.SaveData.PS5.Core.DirName dirName = new Unity.SaveData.PS5.Core.DirName();
			dirName.Data = kMountDirName;

			request.UserId = m_userID;
			request.DirName = dirName;

			Unity.SaveData.PS5.Core.EmptyResponse response = new Unity.SaveData.PS5.Core.EmptyResponse();

			int requestID = Unity.SaveData.PS5.Delete.Deleting.Delete(request, response);
			Debug.Log("[SaveDataTask] Requested Delete : " + requestID);
		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			Debug.LogException(e);
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}
	}

	void DisplayNoFreeSpaceDialog(ulong requiredFreeBlocks)
	{
		try
		{
			Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogRequest request = new Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogRequest();

			request.UserId = m_userID;
			request.Mode = Unity.SaveData.PS5.Dialog.Dialogs.DialogMode.SystemMsg;
			request.DispType = Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Save;

			request.Animations = new Unity.SaveData.PS5.Dialog.Dialogs.AnimationParam(Unity.SaveData.PS5.Dialog.Dialogs.Animation.On, Unity.SaveData.PS5.Dialog.Dialogs.Animation.On);


			Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageParam msg = new Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageParam();

			msg.SysMsgType = Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageType.NoSpaceContinuable;
			msg.Value = requiredFreeBlocks;


			request.SystemMessage = msg;

			Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogResponse response = new Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogResponse();

			int requestID = Unity.SaveData.PS5.Dialog.Dialogs.OpenDialog(request, response);
			Debug.Log("[SaveDataTask] Requested Dialog NoSpaceContinuable : " + requestID);
		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			Debug.LogException(e);
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}
	}
	void DisplayNoFreeSpaceRestoreDialog()
	{
		try
		{
			Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogRequest request = new Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogRequest();

			request.UserId = m_userID;
			request.Mode = Unity.SaveData.PS5.Dialog.Dialogs.DialogMode.SystemMsg;
			request.DispType = Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Load;

			request.Animations = new Unity.SaveData.PS5.Dialog.Dialogs.AnimationParam(Unity.SaveData.PS5.Dialog.Dialogs.Animation.On, Unity.SaveData.PS5.Dialog.Dialogs.Animation.On);

			Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageParam msg = new Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageParam();
			msg.SysMsgType = (Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageType)15; // SCE_SAVE_DATA_DIALOG_SYSMSG_TYPE_NOSPACE_RESTORE
																					  //msg.Value = requiredFreeBlocks;


			request.SystemMessage = msg;

			// Required free blocks will be automatically calculated by the system based on DirName
			Unity.SaveData.PS5.Core.DirName[] dirNames = new Unity.SaveData.PS5.Core.DirName[1];
			dirNames[0] = new Unity.SaveData.PS5.Core.DirName() { Data = kMountDirName };

			Unity.SaveData.PS5.Dialog.Dialogs.Items items = new Unity.SaveData.PS5.Dialog.Dialogs.Items();
			items.DirNames = dirNames;

			request.Items = items;

			Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogResponse response = new Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogResponse();

			int requestID = Unity.SaveData.PS5.Dialog.Dialogs.OpenDialog(request, response);
			Debug.Log("[SaveDataTask] Requested Dialog NoSpaceRestore : " + requestID);
		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			Debug.LogException(e);
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}
	}

	void DisplayBrokenDataDeletedDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType dialogType, Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageType systemMessage)
	{
		try
		{

			Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogRequest request = new Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogRequest();

			request.UserId = m_userID;
			request.Mode = Unity.SaveData.PS5.Dialog.Dialogs.DialogMode.SystemMsg;
			request.DispType = dialogType;

			request.Animations = new Unity.SaveData.PS5.Dialog.Dialogs.AnimationParam(Unity.SaveData.PS5.Dialog.Dialogs.Animation.On, Unity.SaveData.PS5.Dialog.Dialogs.Animation.On);

			Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageParam msg = new Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageParam();
			msg.SysMsgType = systemMessage;

			Unity.SaveData.PS5.Core.DirName[] dirNames = new Unity.SaveData.PS5.Core.DirName[1];
			dirNames[0] = new Unity.SaveData.PS5.Core.DirName() { Data = kMountDirName };

			Unity.SaveData.PS5.Dialog.Dialogs.Items items = new Unity.SaveData.PS5.Dialog.Dialogs.Items();
			items.DirNames = dirNames;

			request.Items = items;

			Unity.SaveData.PS5.Dialog.Dialogs.OptionParam optionParam = new Unity.SaveData.PS5.Dialog.Dialogs.OptionParam();
			optionParam.Back = Unity.SaveData.PS5.Dialog.Dialogs.OptionBack.Disable;

			request.Option = optionParam;

			request.SystemMessage = msg;

			Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogResponse response = new Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogResponse();

			int requestID = Unity.SaveData.PS5.Dialog.Dialogs.OpenDialog(request, response);
			Debug.Log("[SaveDataTask] Requested Dialog CorruptedAndCreate : " + requestID);
		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			Debug.LogException(e);
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}
	}
	void DisplayBrokenDataRestoreDialog()
	{
		try
		{

			Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogRequest request = new Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogRequest();

			request.UserId = m_userID;
			request.Mode = Unity.SaveData.PS5.Dialog.Dialogs.DialogMode.SystemMsg;
			request.DispType = Unity.SaveData.PS5.Dialog.Dialogs.DialogType.Load;

			request.Animations = new Unity.SaveData.PS5.Dialog.Dialogs.AnimationParam(Unity.SaveData.PS5.Dialog.Dialogs.Animation.On, Unity.SaveData.PS5.Dialog.Dialogs.Animation.On);

			Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageParam msg = new Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageParam();
			msg.SysMsgType = Unity.SaveData.PS5.Dialog.Dialogs.SystemMessageType.Corrupted;

			Unity.SaveData.PS5.Core.DirName[] dirNames = new Unity.SaveData.PS5.Core.DirName[1];
			dirNames[0] = new Unity.SaveData.PS5.Core.DirName() { Data = kMountDirName };

			Unity.SaveData.PS5.Dialog.Dialogs.Items items = new Unity.SaveData.PS5.Dialog.Dialogs.Items();
			items.DirNames = dirNames;

			request.Items = items;

			Unity.SaveData.PS5.Dialog.Dialogs.OptionParam optionParam = new Unity.SaveData.PS5.Dialog.Dialogs.OptionParam();
			optionParam.Back = Unity.SaveData.PS5.Dialog.Dialogs.OptionBack.Disable;

			request.Option = optionParam;

			request.SystemMessage = msg;

			Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogResponse response = new Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogResponse();

			int requestID = Unity.SaveData.PS5.Dialog.Dialogs.OpenDialog(request, response);
			Debug.Log("[SaveDataTask] Requested Dialog CurruptedAndRestore : " + requestID);
		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			Debug.LogException(e);
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}
	}

	void DisplayErrorDialog(Unity.SaveData.PS5.Dialog.Dialogs.DialogType dispType, int errorCode)
	{
		try
		{
			Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogRequest request = new Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogRequest();

			request.UserId = m_userID;
			request.Mode = Unity.SaveData.PS5.Dialog.Dialogs.DialogMode.ErrorCode;
			request.DispType = dispType;

			request.Animations = new Unity.SaveData.PS5.Dialog.Dialogs.AnimationParam(Unity.SaveData.PS5.Dialog.Dialogs.Animation.On, Unity.SaveData.PS5.Dialog.Dialogs.Animation.On);

			Unity.SaveData.PS5.Dialog.Dialogs.ErrorCodeParam errorParam = new Unity.SaveData.PS5.Dialog.Dialogs.ErrorCodeParam();
			errorParam.ErrorCode = errorCode;

			request.ErrorCode = errorParam;

			Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogResponse response = new Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogResponse();

			int requestID = Unity.SaveData.PS5.Dialog.Dialogs.OpenDialog(request, response);
			Debug.Log("[SaveDataTask] Requested Dialog ErrorDialog : " + requestID);
		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			Debug.LogException(e);
			m_waitingForResponse = false;
			m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
		}
	}

	void OnPS5Event_SaveDataAsyncEvent(Unity.SaveData.PS5.Core.SaveDataCallbackEvent callbackEvent)
	{
		// Enquee to dispatch at the Main Thread
		m_saveDataEventsToDispatch.Add(callbackEvent);
	}

	public void UpdateState()
	{
		if (m_saveDataEventsToDispatch.Count > 0)
		{
			Debug.LogError("SAVE DISPATCH:" + m_saveDataEventsToDispatch.Count);
			var callback = m_saveDataEventsToDispatch[0];
			m_saveDataEventsToDispatch.RemoveAt(0);
			OnPS5Event_SaveDataEventDispatcher(callback);
		}
	}

	void OnPS5Event_SaveDataEventDispatcher(Unity.SaveData.PS5.Core.SaveDataCallbackEvent callbackEvent)
	{
		Debug.LogError("[PS4 SaveData] API Called = (" + callbackEvent.ApiCalled + ") : Request Id = (" + callbackEvent.RequestId + ") : Calling User Id = (0x" + callbackEvent.UserId.ToString("X8") + ")");

		try
		{
			if (callbackEvent.Response != null)
			{
				if (callbackEvent.Response.ReturnCodeValue < 0)
				{
					Debug.LogError("[SaveDataTask] Error Response : " + callbackEvent.Response.ConvertReturnCodeToString(callbackEvent.ApiCalled));
				}
				else
				{
					Debug.Log("[SaveDataTask] Response : " + callbackEvent.Response.ConvertReturnCodeToString(callbackEvent.ApiCalled));
				}

				if (callbackEvent.Response.Exception != null)
				{
					if (callbackEvent.Response.Exception is Unity.SaveData.PS5.Core.SaveDataException)
					{
						//Debug.LogError("[SaveDataTask] Response Exception: " + ((Sony.PS5.Dialog.Common..SaveData.SaveDataException)callbackEvent.Response.Exception).ExtendedMessage);
					}
					else
					{
						Debug.LogError("[SaveDataTask] Response Exception: " + callbackEvent.Response.Exception.Message);
					}
				}
			}

			// Dispatch event
			switch (callbackEvent.ApiCalled)
			{
				case Unity.SaveData.PS5.Core.FunctionTypes.Mount:
					{
						var response = callbackEvent.Response as Unity.SaveData.PS5.Mount.Mounting.MountResponse;

						if (response != null)
						{
							m_requiredFreeBlocks = response.RequiredBlocks;
							m_sce_result = response.ReturnCode;
						}

						m_waitingForResponse = false;
					}
					break;

				case FunctionTypes.SaveIcon:
				case FunctionTypes.SetMountParams:
				case Unity.SaveData.PS5.Core.FunctionTypes.Unmount:
				case Unity.SaveData.PS5.Core.FunctionTypes.Delete:
				case Unity.SaveData.PS5.Core.FunctionTypes.NotificationUnmountWithBackup:
				case Unity.SaveData.PS5.Core.FunctionTypes.FileOps:
				case Unity.SaveData.PS5.Core.FunctionTypes.Backup:
				case Unity.SaveData.PS5.Core.FunctionTypes.NotificationBackup:
					{
						if (callbackEvent.Response != null)
						{
							m_sce_result = callbackEvent.Response.ReturnCode;
						}

						m_waitingForResponse = false;

					}
					break;
				case Unity.SaveData.PS5.Core.FunctionTypes.OpenDialog:
					{
						var response = callbackEvent.Response as Unity.SaveData.PS5.Dialog.Dialogs.OpenDialogResponse;

						Unity.SaveData.PS5.Dialog.Dialogs.DialogResult result = response.Result;

						if (result == null)
						{
							Debug.LogError("Error occured when opening dialog");
							m_waitingForResponse = false;
							return;
						}

						/*if (result.CallResult == Sony.PS4.SaveData.Dialogs.DialogCallResults.OK)
						{
							m_sce_result = Sony.PS4.SaveData.ReturnCodes.SUCCESS;
						}
						else
						{
							m_sce_result = Sony.PS4.SaveData.ReturnCodes.SAVE_DATA_ERROR_INTERNAL;
						}*/

						m_sce_result = Unity.SaveData.PS5.Core.ReturnCodes.SUCCESS;
						m_waitingForResponse = false;
					}
					break;
			}

		}
		catch (Unity.SaveData.PS5.Core.SaveDataException e)
		{
			Debug.LogError("OnPS4Event_SaveDataEventDispatcher SaveData Exception = " + e.ExtendedMessage);
		}
		catch (System.Exception e)
		{
			Debug.LogException(e);
		}
	}
}


#endif

