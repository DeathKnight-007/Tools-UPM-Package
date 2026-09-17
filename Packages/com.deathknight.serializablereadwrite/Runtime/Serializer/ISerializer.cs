using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SerializableReadWrite
{
    public interface ISerializer
    {
        int Serialize<T>(T target, byte[] buffer, int offset);
        byte[] Serialize<T>(T target);
        string SerializeToString<T>(T target);
        void Serialize<T>(T target, Stream stream);

        T Deserialize<T>(byte[] buffer, int offset, int count);
        T Deserialize<T>(Stream stream);
        T Deserialize<T>(string content);

        Task<int> SerializeAsync<T>(
            T target,
            byte[] buffer,
            int offset,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task<byte[]> SerializeAsync<T>(
            T target,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task<string> SerializeToStringAsync<T>(
            T target,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task SerializeAsync<T>(
            T target,
            Stream stream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task<T> DeserializeAsync<T>(
            byte[] buffer,
            int offset,
            int count,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task<T> DeserializeAsync<T>(
            Stream stream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task<T> DeserializeAsync<T>(
            string content,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);
    }
}
