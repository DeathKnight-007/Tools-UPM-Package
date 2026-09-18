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

        int Serialize(object target, byte[] buffer, int offset);
        byte[] Serialize(object target);
        string SerializeToString(object target);
        void Serialize(object target, Stream stream);

        T Deserialize<T>(byte[] buffer, int offset, int count);
        T Deserialize<T>(Stream stream);
        T Deserialize<T>(string content);

        object Deserialize(byte[] buffer, int offset, int count);
        object Deserialize(Stream stream);
        object Deserialize(string content);

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

        Task<int> SerializeAsync(
            object target,
            byte[] buffer,
            int offset,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task<byte[]> SerializeAsync(
            object target,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task<string> SerializeToStringAsync(
            object target,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task SerializeAsync(
            object target,
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

        Task<object> DeserializeAsync(
            byte[] buffer,
            int offset,
            int count,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task<object> DeserializeAsync(
            Stream stream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task<object> DeserializeAsync(
            string content,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);
    }
}
