using System;
using System.IO;
using SerializableReadWrite;
using UnityEngine;

public sealed class ProtectedSaveExample : MonoBehaviour
{
    private string SavePath => Path.Combine(Application.persistentDataPath, "player.sav");

    public void Save()
    {
        var data = new PlayerData { Level = 1, PlayerName = "Knight" };
        ObjectSaveRead.Save(SavePath, data, "encryption-password", new HMACVerify(), "verify-key");
    }

    public PlayerData Load()
    {
        return ObjectSaveRead.Read<PlayerData>(
            SavePath,
            "encryption-password",
            new HMACVerify(),
            "verify-key");
    }

    [Serializable]
    public sealed class PlayerData
    {
        public int Level;
        public string PlayerName;
    }
}
