using System.Collections;
using System.Collections.Generic;
using UnityEngine;


#if UNITY_PS4
static public class PSLocalizedPresence
{
    const string presenceTextAssetPath = "Localization/PSPresence";
    static readonly string[] localizationRegions = new string[] {
            "en"
        };

    private static int localizationCount => localizationRegions.Length;

    static TextAsset presenceTextAsset;
    static TextAsset PresenceTextAsset
    {
        get
        {
            if (presenceTextAsset == null)
            {
                presenceTextAsset = Resources.Load<TextAsset>(presenceTextAssetPath);
            }

            return presenceTextAsset;
        }
    }

    static Dictionary<string, Sony.NP.Presence.LocalizedGameStatus[]> localizedPresence;

    static bool initialized = false;

    static void Init()
    {
        if (initialized)
            return;

        localizedPresence = new Dictionary<string, Sony.NP.Presence.LocalizedGameStatus[]>();
        string presenceAssetRawText = PresenceTextAsset.text;
        string[] presenceRawTexts = presenceAssetRawText.Split(new char[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < presenceRawTexts.Length; i++)
        {
            string text = presenceRawTexts[i];
            string[] localizedPresences = text.Split(new char[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            var NPLocalizedStatus = new Sony.NP.Presence.LocalizedGameStatus[localizationCount];

            Debug.Log($"Localization for header: {localizedPresences[0]}");

            for (int j = 0; j < localizationCount; j++)
            {
                var gameStatus = new Sony.NP.Presence.LocalizedGameStatus();
                gameStatus.LanguageCode = localizationRegions[j];
                gameStatus.GameStatus = localizedPresences[j + 1];
                NPLocalizedStatus[j] = gameStatus;
            }

            localizedPresence.Add(localizedPresences[0], NPLocalizedStatus);
        }

        initialized = true;
    }

    static public Sony.NP.Presence.LocalizedGameStatus[] GetPresence(string id)
    {
        Init();

        Debug.Log($"Get Presence ID: {id}");
        return localizedPresence[id];
    }
}
#endif
