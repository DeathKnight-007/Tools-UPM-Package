using System;
using System.IO;

namespace SerializableReadWrite
{
    /// <summary>
    /// 转发所有流操作，并统计成功读取或写入的字节数。
    /// </summary>
    internal sealed class ProgressStream : Stream
    {
        private readonly Stream innerStream;
        private readonly Action<int> onRead;
        private readonly Action<int> onWrite;
        private readonly bool leaveOpen;

        public ProgressStream(
            Stream innerStream,
            Action<int> onRead = null,
            Action<int> onWrite = null,
            bool leaveOpen = true)
        {
            this.innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
            this.onRead = onRead;
            this.onWrite = onWrite;
            this.leaveOpen = leaveOpen;
        }

        public override bool CanRead => innerStream.CanRead;

        public override bool CanSeek => innerStream.CanSeek;

        public override bool CanWrite => innerStream.CanWrite;

        public override long Length => innerStream.Length;

        public override long Position
        {
            get => innerStream.Position;
            set => innerStream.Position = value;
        }

        public override void Flush()
        {
            innerStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int readCount = innerStream.Read(buffer, offset, count);
            onRead?.Invoke(readCount);
            return readCount;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return innerStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            innerStream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            innerStream.Write(buffer, offset, count);
            onWrite?.Invoke(count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen)
                innerStream.Dispose();

            base.Dispose(disposing);
        }
    }
}
