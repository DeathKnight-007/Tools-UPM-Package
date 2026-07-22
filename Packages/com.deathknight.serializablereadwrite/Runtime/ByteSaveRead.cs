using System;
using System.IO;

namespace SerializableReadWrite
{
    /// <summary>
    /// 原始 byte[] 与受保护文件之间的读写适配器。
    /// </summary>
    public static class ByteSaveRead
    {
        public static void Save(
            string path,
            byte[] data,
            string passward = null,
            IVerify verify = null,
            string verifyPassward = null)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            ProtectedFile.Write(
                path,
                plaintextStream => plaintextStream.Write(data, 0, data.Length),
                CreateOptions(passward, verify, verifyPassward));
        }

        public static byte[] Read(
            string path,
            string passward = null,
            IVerify verify = null,
            string verifyPassward = null)
        {
            return ProtectedFile.Read(
                path,
                plaintextStream =>
                {
                    using var memoryStream = new MemoryStream();
                    plaintextStream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                },
                CreateOptions(passward, verify, verifyPassward));
        }

        private static ProtectedFileOptions CreateOptions(
            string passward,
            IVerify verify,
            string verifyPassward)
        {
            return new ProtectedFileOptions
            {
                EncryptionPassword = passward,
                Verify = verify,
                VerifyKey = verifyPassward
            };
        }
    }
}
