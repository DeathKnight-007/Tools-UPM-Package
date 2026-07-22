using System.IO;
using SerializableReadWrite;
using UnityEngine;

public sealed class FileArchiveExample : MonoBehaviour
{
    public void CompressPersistentData()
    {
        string archivePath = Path.Combine(Application.temporaryCachePath, "persistent-data.zip");
        FileArchive.CompressDirectory(Application.persistentDataPath, archivePath);
    }

    public void ExtractToTemporaryCache()
    {
        string archivePath = Path.Combine(Application.temporaryCachePath, "persistent-data.zip");
        string outputPath = Path.Combine(Application.temporaryCachePath, "extracted");
        FileArchive.ExtractToDirectory(archivePath, outputPath);
    }
}
