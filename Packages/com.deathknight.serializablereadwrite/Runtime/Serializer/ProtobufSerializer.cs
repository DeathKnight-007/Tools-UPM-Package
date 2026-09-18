using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProtoBuf;

namespace SerializableReadWrite
{
    /// <summary>
    /// 使用 protobuf-net 序列化数据。
    /// 字符串重载使用 Base64 表示 Protobuf 二进制数据。
    /// </summary>
    public class ProtobufSerializer : ISerializer
    {
        public int Serialize<T>(T target, byte[] buffer, int offset)
        {
            ValidateDestination(buffer, offset);
            byte[] data = Serialize(target);
            if (data.Length > buffer.Length - offset)
            {
                throw new ArgumentException(
                    "目标缓冲区剩余空间不足",
                    nameof(buffer));
            }

            Buffer.BlockCopy(data, 0, buffer, offset, data.Length);
            return data.Length;
        }

        public byte[] Serialize<T>(T target)
        {
            using (var stream = new MemoryStream())
            {
                Serializer.Serialize(stream, target);
                return stream.ToArray();
            }
        }

        public string SerializeToString<T>(T target)
        {
            return Convert.ToBase64String(Serialize(target));
        }

        public void Serialize<T>(T target, Stream stream)
        {
            ValidateWriteStream(stream);
            Serializer.Serialize(stream, target);
        }

        public T Deserialize<T>(byte[] buffer, int offset, int count)
        {
            ValidateSegment(buffer, offset, count);
            using (var stream = new MemoryStream(
                buffer,
                offset,
                count,
                writable: false))
            {
                return Serializer.Deserialize<T>(stream);
            }
        }

        public T Deserialize<T>(Stream stream)
        {
            ValidateReadStream(stream);
            return Serializer.Deserialize<T>(stream);
        }

        public T Deserialize<T>(string content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            byte[] data = Convert.FromBase64String(content);
            return Deserialize<T>(data, 0, data.Length);
        }

        public int Serialize(object target, byte[] buffer, int offset)
        {
            ValidateDestination(buffer, offset);
            byte[] data = Serialize(target);
            if (data.Length > buffer.Length - offset)
            {
                throw new ArgumentException(
                    "目标缓冲区剩余空间不足",
                    nameof(buffer));
            }

            Buffer.BlockCopy(data, 0, buffer, offset, data.Length);
            return data.Length;
        }

        public byte[] Serialize(object target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            using (var stream = new MemoryStream())
            {
                Serializer.NonGeneric.Serialize(stream, target);
                return stream.ToArray();
            }
        }

        public string SerializeToString(object target)
        {
            return Convert.ToBase64String(Serialize(target));
        }

        public void Serialize(object target, Stream stream)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            ValidateWriteStream(stream);
            Serializer.NonGeneric.Serialize(stream, target);
        }

        public object Deserialize(byte[] buffer, int offset, int count)
        {
            ValidateSegment(buffer, offset, count);
            throw CreateMissingTypeException();
        }

        public object Deserialize(Stream stream)
        {
            ValidateReadStream(stream);
            throw CreateMissingTypeException();
        }

        public object Deserialize(string content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            throw CreateMissingTypeException();
        }

        public async Task<int> SerializeAsync<T>(
            T target,
            byte[] buffer,
            int offset,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateDestination(buffer, offset);
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Serializing,
                0,
                -1);

            byte[] data = await SerializeToBytesAsync(
                target,
                cancellationToken).ConfigureAwait(false);

            if (data.Length > buffer.Length - offset)
            {
                throw new ArgumentException(
                    "目标缓冲区剩余空间不足",
                    nameof(buffer));
            }

            Buffer.BlockCopy(data, 0, buffer, offset, data.Length);
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Serializing,
                data.Length,
                data.Length);
            return data.Length;
        }

        public async Task<byte[]> SerializeAsync<T>(
            T target,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Serializing,
                0,
                -1);

            byte[] data = await SerializeToBytesAsync(
                target,
                cancellationToken).ConfigureAwait(false);

            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Serializing,
                data.Length,
                data.Length);
            return data;
        }

        public async Task<string> SerializeToStringAsync<T>(
            T target,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            byte[] data = await SerializeAsync(
                target,
                progress,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Convert.ToBase64String(data);
        }

        public async Task SerializeAsync<T>(
            T target,
            Stream stream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateWriteStream(stream);
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Serializing,
                0,
                -1);

            byte[] data = await SerializeToBytesAsync(
                target,
                cancellationToken).ConfigureAwait(false);

            await AsyncStreamProgress.WriteAsync(
                stream,
                data,
                ReadWriteStage.Serializing,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<T> DeserializeAsync<T>(
            byte[] buffer,
            int offset,
            int count,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateSegment(buffer, offset, count);
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Deserializing,
                0,
                count);

            T result = await Task.Run(
                () => Deserialize<T>(buffer, offset, count),
                cancellationToken).ConfigureAwait(false);

            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Deserializing,
                count,
                count);
            return result;
        }

        public async Task<T> DeserializeAsync<T>(
            Stream stream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateReadStream(stream);
            long totalBytes = AsyncStreamProgress.GetRemainingLength(stream);

            using (var memoryStream = new MemoryStream())
            {
                long processedBytes = await AsyncStreamProgress.CopyToAsync(
                    stream,
                    memoryStream,
                    ReadWriteStage.Deserializing,
                    progress,
                    cancellationToken,
                    totalBytes).ConfigureAwait(false);

                byte[] data = memoryStream.ToArray();
                T result = await Task.Run(
                    () => Deserialize<T>(data, 0, data.Length),
                    cancellationToken).ConfigureAwait(false);

                AsyncStreamProgress.Report(
                    progress,
                    ReadWriteStage.Deserializing,
                    totalBytes >= 0 ? totalBytes : processedBytes,
                    totalBytes);
                return result;
            }
        }

        public async Task<T> DeserializeAsync<T>(
            string content,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            byte[] data = Convert.FromBase64String(content);
            return await DeserializeAsync<T>(
                data,
                0,
                data.Length,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> SerializeAsync(
            object target,
            byte[] buffer,
            int offset,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateDestination(buffer, offset);
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Serializing,
                0,
                -1);

            byte[] data = await SerializeObjectToBytesAsync(
                target,
                cancellationToken).ConfigureAwait(false);

            if (data.Length > buffer.Length - offset)
            {
                throw new ArgumentException(
                    "目标缓冲区剩余空间不足",
                    nameof(buffer));
            }

            Buffer.BlockCopy(data, 0, buffer, offset, data.Length);
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Serializing,
                data.Length,
                data.Length);
            return data.Length;
        }

        public async Task<byte[]> SerializeAsync(
            object target,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Serializing,
                0,
                -1);

            byte[] data = await SerializeObjectToBytesAsync(
                target,
                cancellationToken).ConfigureAwait(false);

            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Serializing,
                data.Length,
                data.Length);
            return data;
        }

        public async Task<string> SerializeToStringAsync(
            object target,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            byte[] data = await SerializeAsync(
                target,
                progress,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Convert.ToBase64String(data);
        }

        public async Task SerializeAsync(
            object target,
            Stream stream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateWriteStream(stream);
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Serializing,
                0,
                -1);

            byte[] data = await SerializeObjectToBytesAsync(
                target,
                cancellationToken).ConfigureAwait(false);

            await AsyncStreamProgress.WriteAsync(
                stream,
                data,
                ReadWriteStage.Serializing,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        public Task<object> DeserializeAsync(
            byte[] buffer,
            int offset,
            int count,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateSegment(buffer, offset, count);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<object>(CreateMissingTypeException());
        }

        public Task<object> DeserializeAsync(
            Stream stream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateReadStream(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<object>(CreateMissingTypeException());
        }

        public Task<object> DeserializeAsync(
            string content,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<object>(CreateMissingTypeException());
        }

        private static Task<byte[]> SerializeToBytesAsync<T>(
            T target,
            CancellationToken cancellationToken)
        {
            return Task.Run(
                () =>
                {
                    using (var stream = new MemoryStream())
                    {
                        Serializer.Serialize(stream, target);
                        return stream.ToArray();
                    }
                },
                cancellationToken);
        }

        private static Task<byte[]> SerializeObjectToBytesAsync(
            object target,
            CancellationToken cancellationToken)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            return Task.Run(
                () =>
                {
                    using (var stream = new MemoryStream())
                    {
                        Serializer.NonGeneric.Serialize(stream, target);
                        return stream.ToArray();
                    }
                },
                cancellationToken);
        }

        private static NotSupportedException CreateMissingTypeException()
        {
            return new NotSupportedException(
                "Protobuf 数据不包含 CLR 类型信息，无法直接反序列化为 object；" +
                "请使用 Deserialize<T> 或 DeserializeAsync<T> 指定目标类型。");
        }

        private static void ValidateDestination(byte[] buffer, int offset)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
        }

        private static void ValidateSegment(
            byte[] buffer,
            int offset,
            int count)
        {
            ValidateDestination(buffer, offset);
            if (count < 0 || count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(count));
        }

        private static void ValidateWriteStream(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite)
                throw new ArgumentException("序列化目标流必须可写", nameof(stream));
        }

        private static void ValidateReadStream(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead)
                throw new ArgumentException("反序列化源流必须可读", nameof(stream));
        }
    }
}
