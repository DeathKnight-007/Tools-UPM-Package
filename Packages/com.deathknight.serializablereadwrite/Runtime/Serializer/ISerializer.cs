using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SerializableReadWrite
{
    public interface ISerializer
    {
        // 串行泛型序列化
        int Serialize<T>(T target, byte[] buffer, int offset);
        byte[] Serialize<T>(T target);
        string SerializeToString<T>(T target);
        void Serialize<T>(T target, Stream stream);

        // 串行object序列化
        int Serialize(object target, byte[] buffer, int offset);
        byte[] Serialize(object target);
        string SerializeToString(object target);
        void Serialize(object target, Stream stream);

        // Type序列化根本没有必要

        // 串行泛型反序列化
        T Deserialize<T>(byte[] buffer, int offset, int count);
        T Deserialize<T>(Stream stream);
        T Deserialize<T>(string content);

        // 串行object反序列化，可能有的不支持
        object Deserialize(byte[] buffer, int offset, int count);
        object Deserialize(Stream stream);
        object Deserialize(string content);

        // 串行Type反序列化
        object Deserialize(Type type, byte[] buffer, int offset, int count);
        object Deserialize(Type type, Stream stream);
        object Deserialize(Type type, string content);

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
            Type type,
            byte[] buffer,
            int offset,
            int count,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task<object> DeserializeAsync(
            Type type,
            Stream stream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default);

        Task<object> DeserializeAsync(
            Type type,
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
