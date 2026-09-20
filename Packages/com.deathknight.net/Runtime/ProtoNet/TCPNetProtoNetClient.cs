using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using SerializableReadWrite;
using System.Net;

namespace DeathKnight.Net
{
    public class TCPNetProtoNetClient : IProtoNetClient<TCPNetHeaderProto>, IDisposable
    {
        /// <summary>
        /// 当收到完整数据
        /// </summary>
        public event Action<(TCPNetHeaderProto, object)> OnReceivedObject;

        /// <summary>
        /// 当收到一帧数据，一般一帧数据就是完整数据了，除非完整数据需要切片
        /// </summary>
        public event Action<long> OnReceivedFrame;

        public event Action<Exception> OnReceivedFail;

        public event Action<Exception> OnSendFail;

        public event Action OnConnected;

        public event Action<Exception> OnConnectedFail;

        public event Action OnDisconnected;

        public int FrameBufferSize { get; }

        public NetClient NetClient { get; }

        public long RequestId
        {
            get
            {
                return requestId;
            }
        }

        public ISerializer Serializer { get; }

        private byte[] writeBuffer; // 发送帧数据缓冲池
        private byte[] readBuffer; // 总接收数据缓冲池
        private Stack<MemoryStream> bigBuffers = new(); // 大块帧数据缓存池
        private Dictionary<long, MemoryStream> bigFrames = new(); // 大块帧数据
        private MemoryStream sendBigFrames = new();
        private long requestId = 0;
        private static object locker = new object();
        public TCPNetProtoNetClient(int frameBufferSize, NetClient netClient, ISerializer serializer)
        {
            if (frameBufferSize <= TCPNetHeaderProto.HeaderLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameBufferSize),
                    frameBufferSize,
                    $"Frame buffer size must be greater than the header length ({TCPNetHeaderProto.HeaderLength}).");
            }

            if (frameBufferSize > int.MaxValue / 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameBufferSize),
                    frameBufferSize,
                    "Frame buffer size is too large.");
            }

            this.NetClient = netClient ?? throw new ArgumentNullException(nameof(netClient));
            this.Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            this.FrameBufferSize = frameBufferSize;
            readBuffer = new byte[FrameBufferSize * 2];
            writeBuffer = new byte[FrameBufferSize];
            sendCancelTokenSource = new();
            receiveCancelTokenSource = new();
        }
        public async Task Dispose()
        {
            Disconnect();
            await sendTask;
            await receiveTask;
            sendTask?.Dispose();
            receiveTask?.Dispose();
            this.NetClient?.Dispose();
            MemoryStream mstream;
            while(bigBuffers.TryPop(out mstream))
            {
                mstream.Dispose();
            }
            foreach(var item in bigFrames)
            {
                item.Value.Dispose();
            }
            sendBigFrames.Dispose();
            sendSignal.Dispose();
        }

        private ConcurrentQueue<(TCPNetHeaderProto Header, object Value)> messageQueue = new();
        private Task sendTask;

        /// <summary>
        /// 不支持多线程发送
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="header"></param>
        public void Send<T>(TCPNetHeaderProto header, T value)
        {
            messageQueue.Enqueue((header, value));
            sendSignal.Release();
        }

        private async Task SendQueue(CancellationToken token)
        {
            if (NetClient == null)
            {
                throw new InvalidOperationException(
                    "net client not initialized or initialization failed");
            }

            (TCPNetHeaderProto Header, object Value) element;
            if (!messageQueue.TryDequeue(out element))
            {
                return;
            }
            TCPNetHeaderProto header = element.Header;
            object target = element.Value;
            if (!header.FragmentInfo.NeedFragment)
            {
                int count = Serializer.Serialize(target, writeBuffer, TCPNetHeaderProto.HeaderLength);
                //写入消息头
                header.RequestId = requestId;
                header.Timestamp = (int)Math.Floor(Time.time * 1000);
                header.PayloadLength = count;
                header.ToBytes(writeBuffer, 0);
                await NetClient.Send(writeBuffer, 0, count + TCPNetHeaderProto.HeaderLength, token);
                requestId++;
            }
            else
            {
                Serializer.Serialize(target, sendBigFrames);
                sendBigFrames.Position = 0;
                header.FragmentInfo.Index = 0;
                header.FragmentInfo.FragmentId = requestId;
                double fragmentCount = Math.Ceiling((double)sendBigFrames.Length / (FrameBufferSize - TCPNetHeaderProto.HeaderLength));
                if(fragmentCount <= 0 || fragmentCount > Int16.MaxValue)
                {
                    throw new ArgumentOutOfRangeException("send data too long can not slice");
                }
                header.FragmentInfo.TotalCount = (short)fragmentCount;
                while (true)
                {
                    int writeLength = (int)Math.Min(writeBuffer.Length - TCPNetHeaderProto.HeaderLength, sendBigFrames.Length - sendBigFrames.Position);
                    sendBigFrames.Read(writeBuffer, TCPNetHeaderProto.HeaderLength, writeLength);
                    //写入消息头
                    header.RequestId = requestId;
                    header.Timestamp = (int)Math.Floor(Time.time * 1000);
                    header.PayloadLength = writeLength;
                    header.FragmentInfo.Index++;
                    header.ToBytes(writeBuffer, 0);
                    await NetClient.Send(writeBuffer, 0, writeLength + TCPNetHeaderProto.HeaderLength, token);
                    requestId++;
                    if (sendBigFrames.Position >= sendBigFrames.Length)
                    {
                        break;
                    }
                }
                sendBigFrames.SetLength(0);
            }
        }
        private readonly SemaphoreSlim sendSignal = new(0);
        private CancellationTokenSource sendCancelTokenSource;
        private async Task SendLoopAsync()
        {
            var token = sendCancelTokenSource.Token;

            try
            {
                while (true)
                {
                    await sendSignal.WaitAsync(token);

                    if (messageQueue.Count <= 0)
                    {
                        continue;
                    }

                    try
                    {
                        await SendQueue(token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {

            }
            catch (Exception e)
            {
                Debug.LogError(e);
                Disconnect();
                OnSendFail?.Invoke(e);
            }
        }

        private int availableStart = 0, availableEnd = 0;
        private Task receiveTask;
        private async Task<(TCPNetHeaderProto, object)> Receive(CancellationToken token)
        {
            bool gotHeader = false;
            (TCPNetHeaderProto Header, object Value) item = new();
            while (true)
            {
                int availableLength = availableEnd - availableStart;

                // 接收到的数据超过消息头了，则开始解析有效消息段
                if (availableLength >= TCPNetHeaderProto.HeaderLength)
                {
                    if (!gotHeader)
                    {
                        TCPNetHeaderProto header = TCPNetHeaderProto.GetProto(readBuffer, availableStart);
                        if (!header.Valid())
                        {
                            throw new Exception("error header");
                        }
                        item.Header = header;
                        gotHeader = true;
                    }

                    if (item.Header.PayloadLength < 0 || item.Header.PayloadLength > FrameBufferSize - TCPNetHeaderProto.HeaderLength)
                    {
                        throw new InvalidDataException(
                            $"Invalid payload length: {item.Header.PayloadLength}");
                    }
                    if (availableLength >= item.Header.PayloadLength + TCPNetHeaderProto.HeaderLength)
                    {
                        OnReceivedFrame?.Invoke(item.Header.RequestId);
                        if (item.Header.FragmentInfo.NeedFragment)
                        {
                            if (item.Header.FragmentInfo.TotalCount <= 0 || item.Header.FragmentInfo.Index <= 0 || item.Header.FragmentInfo.Index > item.Header.FragmentInfo.TotalCount)
                            {
                                throw new InvalidDataException("Invalid fragment metadata.");
                            }
                            if (!bigFrames.ContainsKey(item.Header.FragmentInfo.FragmentId))
                            {
                                MemoryStream mstream;
                                if (bigBuffers.TryPop(out mstream))
                                {
                                }
                                else
                                {
                                    mstream = new MemoryStream();
                                    mstream.Capacity = FrameBufferSize * 2;
                                }
                                bigFrames.Add(item.Header.FragmentInfo.FragmentId, mstream);
                            }
                            MemoryStream getstream = bigFrames[item.Header.FragmentInfo.FragmentId];
                            getstream.Write(readBuffer, availableStart + TCPNetHeaderProto.HeaderLength, item.Header.PayloadLength);
                            availableStart += TCPNetHeaderProto.HeaderLength + item.Header.PayloadLength;
                            if (item.Header.FragmentInfo.Index == item.Header.FragmentInfo.TotalCount)
                            {
                                getstream.Position = 0;
                                item.Value = Serializer.Deserialize(getstream);
                                bigFrames.Remove(item.Header.FragmentInfo.FragmentId);
                                getstream.SetLength(0);
                                bigBuffers.Push(getstream);
                                return item;
                            }
                        }
                        else
                        {
                            item.Value = Serializer.Deserialize(readBuffer, availableStart + TCPNetHeaderProto.HeaderLength, item.Header.PayloadLength);
                            availableStart += TCPNetHeaderProto.HeaderLength + item.Header.PayloadLength;
                            return item;
                        }
                        gotHeader = false;
                    }

                }
                if (availableEnd >= readBuffer.Length) // 如果已经读满了整理一下
                {
                    if (availableStart < availableEnd)
                    {
                        int leftlength = availableEnd - availableStart;
                        Buffer.BlockCopy(readBuffer, availableStart, readBuffer, 0, leftlength);
                        availableStart = 0;
                        availableEnd = leftlength;
                    }
                    else
                    {
                        availableStart = 0;
                        availableEnd = 0;
                    }
                }
                // 接收一段有效信息
                int count = await NetClient.Receive(readBuffer, availableEnd, token);
                if (count == 0)
                {
                    Disconnect();
                    throw new EndOfStreamException("server fin");
                }
                availableEnd += count;
            }
        }

        private CancellationTokenSource receiveCancelTokenSource;
        private async Task ReceiveLoopAsync()
        {
            var token = receiveCancelTokenSource.Token;

            try
            {
                while (true)
                {
                    try
                    {
                        (TCPNetHeaderProto, object) result = await Receive(token);
                        OnReceivedObject?.Invoke(result);
                    }
                    catch (EndOfStreamException)
                    {

                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Debug.LogWarning("receive cancelled");
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                Disconnect();
                OnReceivedFail?.Invoke(e);
            }
        }

        private bool canConnect = true;
        public async Task Connect(DnsEndPoint dnsEndPoint)
        {
            try
            {
                if(!canConnect)
                {
                    throw new Exception("TCPNetProtoNetClient can not reuse");
                }    
                canConnect = false;
                await NetClient.Connect(dnsEndPoint);
                Volatile.Write(ref connected, 1);
            }
            catch (Exception e)
            {
                Debug.LogError(e); // 连接失败报错
                OnConnectedFail?.Invoke(e);
            }
            if (connected == 1)
            {
                receiveTask = Task.Run(ReceiveLoopAsync);
                sendTask = Task.Run(SendLoopAsync);
                OnConnected?.Invoke();
            }
        }

        public async Task Connect(IPEndPoint ipEndPoint)
        {
            try
            {
                if (!canConnect)
                {
                    throw new Exception("TCPNetProtoNetClient can not reuse");
                }
                canConnect = false;
                await NetClient.Connect(ipEndPoint);
                Volatile.Write(ref connected, 1);
            }
            catch (Exception e)
            {
                Debug.LogError(e); // 连接失败报错
                OnConnectedFail?.Invoke(e);
            }
            if (connected == 1)
            {
                receiveTask = Task.Run(ReceiveLoopAsync);
                sendTask = Task.Run(SendLoopAsync);
                OnConnected?.Invoke();
            }
        }

        public async Task Connect(IPEndPoint[] ipEndPoints)
        {
            try
            {
                if (!canConnect)
                {
                    throw new Exception("TCPNetProtoNetClient can not reuse");
                }
                canConnect = false;
                await NetClient.Connect(ipEndPoints);
                Volatile.Write(ref connected, 1);
            }
            catch (Exception e)
            {
                Debug.LogError(e); // 连接失败报错
                OnConnectedFail?.Invoke(e);
            }
            if (connected == 1)
            {
                receiveTask = Task.Run(ReceiveLoopAsync);
                sendTask = Task.Run(SendLoopAsync);
                OnConnected?.Invoke();
            }
        }

        public void Disconnect()
        {
            DisconnectCore();
        }

        private int connected;

        private void DisconnectCore()
        {
            if (Interlocked.Exchange(ref connected, 0) != 1)
                return;

            sendCancelTokenSource.Cancel();
            receiveCancelTokenSource.Cancel();

            try
            {
                NetClient.Disconnect();
            }
            catch (Exception closeException)
            {
                
            }

            OnDisconnected?.Invoke();
        }

        void IDisposable.Dispose()
        {
            _ = Dispose();
        }
    }
}
