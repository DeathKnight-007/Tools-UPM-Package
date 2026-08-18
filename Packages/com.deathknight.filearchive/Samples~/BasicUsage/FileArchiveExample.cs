using System.IO;
using SerializableReadWrite;
using UnityEngine;

public sealed class FileArchiveExample : MonoBehaviour
{
    public void CompressFiles()
    {
        string archivePath = Path.Combine(Application.temporaryCachePath, "persistent-data.zip");
        string[] sourceFiles =
        {
            Path.Combine(Application.persistentDataPath, "settings.json"),
            Path.Combine(Application.persistentDataPath, "save.dat")
        };

        FileArchive.CompressFiles(sourceFiles, archivePath);
    }

    public void ExtractToTemporaryCache()
    {
        string archivePath = Path.Combine(Application.temporaryCachePath, "persistent-data.zip");
        string outputPath = Path.Combine(Application.temporaryCachePath, "extracted");
        FileArchive.ExtractToDirectory(archivePath, outputPath);
    }
}
