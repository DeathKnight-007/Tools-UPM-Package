using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SerializableReadWrite
{
    /// <summary>
    /// 验证数据完整性以及是否被修改。
    /// </summary>
    public interface IVerify
    {
        bool NeedPassword { get; }
        int TagLength { get; }

        byte[] ComputeTag(byte[] data, byte[] passward = null);
        bool VerifyTag(byte[] data, byte[] tag, byte[] passward = null);
        byte[] ComputeTag(Stream data, byte[] passward = null);
        bool VerifyTag(Stream data, byte[] tag, byte[] passward = null);

        Task<byte[]> ComputeTagAsync(
            Stream data,
            byte[] passward = null,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task<bool> VerifyTagAsync(
            Stream data,
            byte[] tag,
            byte[] passward = null,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);
    }
}
