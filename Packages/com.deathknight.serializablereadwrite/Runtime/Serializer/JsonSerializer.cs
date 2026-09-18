using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SerializableReadWrite
{
    public class JsonSerializer : ISerializer
    {
        private readonly UTF8Encoding utf8 = new UTF8Encoding(false);

        public T Deserialize<T>(byte[] buffer, int offset, int count)
        {
            string content = utf8.GetString(buffer, offset, count);
            return JsonConvert.DeserializeObject<T>(content);
        }

        public T Deserialize<T>(Stream stream)
        {
            using (var streamReader = new StreamReader(
                stream,
                utf8,
                false,
                1024 * 16,
                true))
            using (var jsonReader = new JsonTextReader(streamReader))
            {
                jsonReader.CloseInput = false;
                return Newtonsoft.Json.JsonSerializer
                    .CreateDefault()
                    .Deserialize<T>(jsonReader);
            }
        }

        public T Deserialize<T>(string content)
        {
            return JsonConvert.DeserializeObject<T>(content);
        }

        public int Serialize<T>(T target, byte[] buffer, int offset)
        {
            string content = JsonConvert.SerializeObject(target);
            return utf8.GetBytes(
                content,
                0,
                content.Length,
                buffer,
                offset);
        }

        public byte[] Serialize<T>(T target)
        {
            string content = JsonConvert.SerializeObject(target);
            return utf8.GetBytes(content);
        }

        public void Serialize<T>(T target, Stream stream)
        {
            using (var streamWriter = new StreamWriter(
                stream,
                utf8,
                1024 * 16,
                true))
            using (var jsonWriter = new JsonTextWriter(streamWriter))
            {
                jsonWriter.CloseOutput = false;
                Newtonsoft.Json.JsonSerializer
                    .CreateDefault()
                    .Serialize(jsonWriter, target);
            }
        }

        public string SerializeToString<T>(T target)
        {
            return JsonConvert.SerializeObject(target);
        }

        public int Serialize(object target, byte[] buffer, int offset)
        {
            return Serialize<object>(target, buffer, offset);
        }

        public byte[] Serialize(object target)
        {
            return Serialize<object>(target);
        }

        public string SerializeToString(object target)
        {
            return SerializeToString<object>(target);
        }

        public void Serialize(object target, Stream stream)
        {
            Serialize<object>(target, stream);
        }

        public object Deserialize(byte[] buffer, int offset, int count)
        {
            return Deserialize<object>(buffer, offset, count);
        }

        public object Deserialize(Stream stream)
        {
            return Deserialize<object>(stream);
        }

        public object Deserialize(string content)
        {
            return Deserialize<object>(content);
        }

        public async Task<int> SerializeAsync<T>(
            T target,
            byte[] buffer,
            int offset,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

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
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Serializing,
                0,
                -1);

            string content = await Task.Run(
                () => JsonConvert.SerializeObject(target),
                cancellationToken).ConfigureAwait(false);

            int byteCount = utf8.GetByteCount(content);
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Serializing,
                byteCount,
                byteCount);
            return content;
        }

        public async Task SerializeAsync<T>(
            T target,
            Stream stream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite)
                throw new ArgumentException("序列化目标流必须可写", nameof(stream));

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
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead)
                throw new ArgumentException("反序列化源流必须可读", nameof(stream));

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

            int byteCount = utf8.GetByteCount(content);
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Deserializing,
                0,
                byteCount);

            T result = await Task.Run(
                () => JsonConvert.DeserializeObject<T>(content),
                cancellationToken).ConfigureAwait(false);

            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.Deserializing,
                byteCount,
                byteCount);
            return result;
        }

        public Task<int> SerializeAsync(
            object target,
            byte[] buffer,
            int offset,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return SerializeAsync<object>(
                target,
                buffer,
                offset,
                progress,
                cancellationToken);
        }

        public Task<byte[]> SerializeAsync(
            object target,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return SerializeAsync<object>(
                target,
                progress,
                cancellationToken);
        }

        public Task<string> SerializeToStringAsync(
            object target,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return SerializeToStringAsync<object>(
                target,
                progress,
                cancellationToken);
        }

        public Task SerializeAsync(
            object target,
            Stream stream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return SerializeAsync<object>(
                target,
                stream,
                progress,
                cancellationToken);
        }

        public Task<object> DeserializeAsync(
            byte[] buffer,
            int offset,
            int count,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return DeserializeAsync<object>(
                buffer,
                offset,
                count,
                progress,
                cancellationToken);
        }

        public Task<object> DeserializeAsync(
            Stream stream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return DeserializeAsync<object>(
                stream,
                progress,
                cancellationToken);
        }

        public Task<object> DeserializeAsync(
            string content,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return DeserializeAsync<object>(
                content,
                progress,
                cancellationToken);
        }

        private async Task<byte[]> SerializeToBytesAsync<T>(
            T target,
            CancellationToken cancellationToken)
        {
            return await Task.Run(
                () =>
                {
                    string content = JsonConvert.SerializeObject(target);
                    return utf8.GetBytes(content);
                },
                cancellationToken).ConfigureAwait(false);
        }

        private static void ValidateSegment(
            byte[] buffer,
            int offset,
            int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(count));
        }
    }
}
