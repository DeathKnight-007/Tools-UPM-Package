using SerializableReadWrite;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering.VirtualTexturing;

namespace DeathKnight.Net
{
    public class MyTcpClient<T, U> : IMyTcpClient<T> where T : BaseProto, new() where U : NetSimpleClient, new()
    {
        public IPEndPoint ClientIPEndPoint { get; private set; }

        public IPEndPoint ServerIpEndPoint { get; private set; }

        public int FrameBufferSize { get; private set; }

        public ISerializer Serializer { get; private set; }

        public NetClientConfig Config { get; private set; }

        public event Action<(T, object)> OnReceivedObject;
        public event Action<long> OnReceivedFrame;
        public event Action<Exception> OnReceivedFail;
        public event Action<Exception> OnSendFail;
        public event Action OnConnected;
        public event Action<Exception> OnConnectedFail;
        public event Action OnDisconnected;


        private byte[] writeBuffer; // 发送帧数据缓冲池，64k之上已经可以算是大堆了，使用可以回收byte[]
        private byte[] readBuffer; // 总接收数据缓冲池,，64k之上已经可以算是大堆了，使用可以回收byte[]

        private long requestId = 0;
        private T t = new();
        private U activeClient;
        private InvokeOnMainThreadHelper invokeHelper;
        public void Init(NetClientConfig config, int frameBufferSize, ISerializer serializer)
        {
            if (frameBufferSize <= t.HeaderLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameBufferSize),
                    frameBufferSize,
                    $"Frame buffer size must be greater than the header length ({t.HeaderLength}).");
            }

            if (frameBufferSize > int.MaxValue / 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameBufferSize),
                    frameBufferSize,
                    "Frame buffer size is too large.");
            }

            this.Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            this.FrameBufferSize = frameBufferSize;
            this.Config = config;
            invokeHelper = new();
        }

        enum Progress
        {
            None = 0,     // 没开始呢
            Doing = 1 << 1,    // 正在干
            Completed = 1 << 2, // 结束了
            Error = 1 << 3,     // 出错了
        }
        enum ConnectedState
        {
            None = 0,
            SendConnected = 1 << 1,
            ReceiveConnected = 1 << 2,
        }

        private Progress connectProgress = Progress.None;
        private ConnectedState connectedState = ConnectedState.None;

        private bool CheckCanConnect()
        {
            if (connectedState != ConnectedState.None)
            {
                return false;
            }
            if(connectProgress == Progress.Doing)
            {
                return false;
            }
            if (sendTask != null && !sendTask.IsCompleted)
                return false;
            if (receiveTask != null && !receiveTask.IsCompleted)
                return false;
            return true;
        }
        private bool connectBlockError = false;
        public async Task Connect(IPEndPoint ip)
        {
            if (!CheckCanConnect())
            {
                Debug.LogError("can't connect now");
                return;
            }

            ClientIPEndPoint = null;
            ServerIpEndPoint = null;

            U client = new();
            client.Init(Config);
            try
            {
                Func<Task> connect = () =>
                {
                    return client.Connect(ip);
                };
                activeClient = client;
                await ConnectCore(connect);
            }
            catch(Exception e)
            {
                client.Dispose();
                activeClient = null;
                connectProgress = Progress.Error;
                if (!connectBlockError)
                {
                    Debug.LogException(e);
                    invokeHelper.InvokeOnMainThreadPost<Exception>((ex) => { OnConnectedFail(ex); }, e);
                }
            }
        }

        public async Task Connect(string host, int port)
        {
            if (!CheckCanConnect())
            {
                Debug.LogError("can't connect now");
                return;
            }

            ClientIPEndPoint = null;
            ServerIpEndPoint = null;

            U client = new();
            client.Init(Config);
            try
            {
                Func<Task> connect = () =>
                {
                    return client.Connect(host, port);
                };
                activeClient = client;
                await ConnectCore(connect);
            }
            catch(Exception e)
            {
                client.Dispose();
                activeClient = null;
                connectProgress = Progress.Error;
                if (!connectBlockError)
                {
                    Debug.LogException(e);
                    invokeHelper.InvokeOnMainThreadPost<Exception>((ex) => { OnConnectedFail(ex); }, e);
                }
            }
        }

        private Task sendTask, receiveTask;
        private async Task ConnectCore(Func<Task> connect)
        {
            connectBlockError = false;
            connectProgress = Progress.Doing;
            await connect();

            if (activeClient == null)
            {
                connectProgress = Progress.Completed;
                return;
            }
            connectProgress = Progress.Completed;
            connectedState = ConnectedState.SendConnected | ConnectedState.ReceiveConnected;
                
            ClientIPEndPoint = activeClient.ClientIPEndPoint;
            ServerIpEndPoint = activeClient.ServerIpEndPoint;

            sendTask = StartSendLoopAsync();
            receiveTask = StartReceiveLoopAsync();

            invokeHelper.InvokeOnMainThreadPost(OnConnected);
        }

        private async Task StartSendLoopAsync()
        {
            SendLoopAsyncPrepare();
            CancellationToken token = sendCancelTokenSource.Token;
            Exception error = null;
            try
            {
                await Task.Run(() => SendLoopAsync());
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Debug.LogWarning("send canceled");
            }
            catch (Exception e)
            {
                error = e;
            }
            SendLoopAsyncDispose();
            bool notify = !BlockSendError;
            StopAllLoopAsync();
            if (error != null)
            {
                if (notify)
                {
                    // 发送异常结束
                    Debug.LogException(error);
                    try
                    {
                        // 触发发送异常回调
                        OnSendFail(error);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }

        }

        // 请求发送循环停止
        private void StopSendLoopAsync(Action onComplete = null)
        {
            BlockSendError = true;
            sendCancelTokenSource?.Cancel();
            if(sendTask != null)
            {
                sendTask.GetAwaiter().OnCompleted(() => { 
                    if(onComplete != null)
                    {
                        onComplete();
                    }
                });
            }
        }
        private async Task StartReceiveLoopAsync()
        {
            ReceiveLoopAsyncPrepare();
            CancellationToken token = receiveCancelTokenSource.Token;
            Exception error = null;
            try
            {
                await Task.Run(() => ReceiveLoopAsync());
            }
            catch (EndOfStreamException)
            {

            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Debug.LogWarning("receive cancel");
            }
            catch (Exception e)
            {
                error = e;
            }
            ReceiveLoopAsyncDispose();
            bool notify = !BlockReceiveError;
            StopAllLoopAsync();
            if (error != null)
            {
                if (notify)
                {
                    Debug.LogException(error);
                    try
                    {
                        OnReceivedFail(error);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        /// <summary>
        /// 请求接受循环停止
        /// </summary>
        /// <param name="onComplete"></param>
        private void StopReceiveLoopAsync(Action onComplete = null)
        {
            BlockReceiveError = true;
            receiveCancelTokenSource?.Cancel();
            receiveTask.GetAwaiter().OnCompleted(() => {
                if (onComplete != null)
                    onComplete();
            });
        }

        private void StopAllLoopAsync()
        {
            // 请求发送循环终止
            Disconnect();
            // 请求接受循环终止
            if (connectedState.HasFlag(ConnectedState.ReceiveConnected))
            {
                connectedState -= ConnectedState.ReceiveConnected;
                StopReceiveLoopAsync();
                
                // 检查是否完全断开了
                if (connectedState == ConnectedState.None)
                {
                    activeClient?.Dispose();
                    activeClient = null;
                    _ = OnDisconnectedHelp();
                }
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Disconnect()
        {
            // 状态提前修改，防止继续发送
            if (connectedState.HasFlag(ConnectedState.SendConnected))
            {
                connectedState -= ConnectedState.SendConnected;
            }
            else
            {
                Debug.LogWarning("disconnect warning: client has shutdown");
                return;
            }
            messageQueue.Clear();
            // 请求终止发送异步操作
            StopSendLoopAsync();
            // 发送FIN
            try
            {
                activeClient.Disconnect();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            // 如果连接状态全没了
            if (connectedState == ConnectedState.None)
            {
                activeClient?.Dispose();
                activeClient = null;
                _ = OnDisconnectedHelp();
            }
        }

        private async Task OnDisconnectedHelp()
        {
            await Task.WhenAll(sendTask, receiveTask);
            try
            {
                bytesObjectTransfer?.Dispose();
                bytesObjectTransfer = null;
                activeClient?.Dispose();
                activeClient = null;
                OnDisconnected();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        public void Dispose()
        {
            StopAllLoopAsync();
            if (activeClient != null)
            {
                connectBlockError = true;
                activeClient.Dispose();
                activeClient = null;
            }
        }

        private ConcurrentQueue<(T Header, object Value)> messageQueue = new();
        private SemaphoreSlim sendSignal;
        public void Send<M>(M data, T header)
        {
            if (!connectedState.HasFlag(ConnectedState.SendConnected))
            {
                Debug.LogError($"send has closed:{header.ToString()}");
                return;
            }
            messageQueue.Enqueue((header, data));
            sendSignal.Release();
        }

        /// <summary>
        /// 发送队列里的一个消息
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private async Task SendQueueOnce(CancellationToken token)
        {
            (T Header, object Value) element;
            if (!messageQueue.TryDequeue(out element))
            {
                return;
            }
            T header = element.Header;
            object target = element.Value;
            if (!header.FragmentInfo.NeedFragment)
            {
                int count = bytesObjectTransfer.ParseObjectToBytes(target, writeBuffer, t.HeaderLength);
                if (count > FrameBufferSize - t.HeaderLength)
                {
                    throw new InvalidDataException("Payload exceeds frame size; enable fragmentation.");
                }
                //写入消息头
                header.RequestId = requestId;
                header.Timestamp = Environment.TickCount;// 操作系统启动到现在经过的毫秒数
                header.PayloadLength = count;
                bytesObjectTransfer.ParseHeaderToBytes(header, writeBuffer, 0);
                await activeClient.Send(writeBuffer, 0, count + t.HeaderLength, token);
                requestId++;
            }
            else
            {
                bytesObjectTransfer.ParseObjectToBytesFragment(target, header);
                bool hasNext = true;
                while (hasNext)
                {
                    header.RequestId = requestId;
                    header.Timestamp = Environment.TickCount;// 操作系统启动到现在经过的毫秒数
                    hasNext = bytesObjectTransfer.ParseObjectToBytesFragmentNext(target, header, writeBuffer);
                    await activeClient.Send(writeBuffer, 0, header.PayloadLength + t.HeaderLength, token);
                    requestId++;
                }
            }
        }

        private void SendLoopAsyncPrepare()
        {
            // 写入缓冲
            writeBuffer = ArrayPool<byte>.Shared.Rent(FrameBufferSize);
            // 发送信号
            sendSignal = new SemaphoreSlim(0);
            // 对象序列化工具
            if(bytesObjectTransfer == null)
                bytesObjectTransfer = new ParseBytesObject<T>(Serializer, FrameBufferSize);
            // 取消token
            sendCancelTokenSource = new();
            //
            BlockSendError = false;
        }
        private void SendLoopAsyncDispose()
        {
            //
            if (writeBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(writeBuffer);
                writeBuffer = null;
            }
            //
            sendSignal?.Dispose();
            sendSignal = null;
            //
            sendCancelTokenSource?.Dispose();
            sendCancelTokenSource = null;
        }

        private bool BlockSendError = false;
        private CancellationTokenSource sendCancelTokenSource;
        private async Task SendLoopAsync()
        {
            CancellationToken token = sendCancelTokenSource.Token;
            while (true)
            {
                await sendSignal.WaitAsync(token);
                await SendQueueOnce(token);
            }
        }

        private int availableStart = 0, availableEnd = 0;
        private ParseBytesObject<T> bytesObjectTransfer;
        private async Task ReceiveOnce(CancellationToken token)
        {
            if (availableEnd >= FrameBufferSize) // 如果已经读满了整理一下
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
            int count = await activeClient.Receive(readBuffer, availableEnd, FrameBufferSize - availableEnd, token);
            if (token.IsCancellationRequested)
            {
                throw new OperationCanceledException("Receive cancel");
            }
            if (count == 0)
            {
                throw new EndOfStreamException("server fin");
            }
            availableEnd += count;
        }

        private async Task<(T, object)> Receive(CancellationToken token)
        {
            (T Header, object Value) item = new();
            // 先解析消息头
            while (true)
            {
                int availableLength = availableEnd - availableStart;

                // 接收到的数据超过消息头了，则开始解析有效消息段
                if (availableLength >= t.HeaderLength)
                {
                    T header = bytesObjectTransfer.ParseHeaderFromBytes(readBuffer, availableStart, t.HeaderLength);
                    item.Header = header;

                    if (item.Header.PayloadLength > FrameBufferSize - t.HeaderLength || header.PayloadLength < 0)
                    {
                        throw new InvalidDataException(
                            $"Invalid payload length: {item.Header.PayloadLength}");
                    }
                    break;
                }
                await ReceiveOnce(token);
            }

            if (!item.Header.FragmentInfo.NeedFragment)
            {
                while (true)
                {
                    int availableLength = availableEnd - availableStart;
                    if (availableLength >= item.Header.PayloadLength + t.HeaderLength)
                    {
                        item.Value = bytesObjectTransfer.ParseObjectFromBytes(item.Header, readBuffer, availableStart + item.Header.HeaderLength, item.Header.PayloadLength);
                        availableStart += t.HeaderLength + item.Header.PayloadLength;
                        invokeHelper.InvokeOnMainThreadPost<long>(OnReceivedFrame, item.Header.RequestId);
                        return item;
                    }
                    await ReceiveOnce(token);
                }
            }
            else
            {
                T header = null;
                while (true)
                {
                    int availableLength = availableEnd - availableStart;

                    // 解析消息头
                    if (header == null && availableLength >= t.HeaderLength)
                    {
                        header = bytesObjectTransfer.ParseHeaderFromBytes(readBuffer, availableStart, t.HeaderLength);

                        if (header.PayloadLength > FrameBufferSize - t.HeaderLength || header.PayloadLength < 0)
                        {
                            throw new InvalidDataException($"Invalid payload length: {header.PayloadLength}");
                        }
                    }
                    // 解析消息体
                    if (header != null && availableLength >= header.PayloadLength + t.HeaderLength)
                    {
                        object result;
                        bool finish = bytesObjectTransfer.ParseObjectFromBytesFragment(header, readBuffer, availableStart + t.HeaderLength, header.PayloadLength, out result);
                        invokeHelper.InvokeOnMainThreadPost<long>(OnReceivedFrame, header.RequestId);
                        availableStart += t.HeaderLength + header.PayloadLength;
                        if (finish)
                        {
                            item.Value = result;
                            return item;
                        }
                        header = null;
                    }
                    else
                    {
                        await ReceiveOnce(token);
                    }
                }
            }
        }

        private void ReceiveLoopAsyncPrepare()
        {
            // 分配缓冲
            readBuffer = ArrayPool<byte>.Shared.Rent(FrameBufferSize);
            // 分配取消token
            receiveCancelTokenSource = new();
            // 序列化工具
            if (bytesObjectTransfer == null)
            {
                bytesObjectTransfer = new ParseBytesObject<T>(Serializer, FrameBufferSize);
            }
            //
            BlockReceiveError = false;
        }

        private void ReceiveLoopAsyncDispose()
        {
            // 分配缓冲
            if (readBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(readBuffer);
                readBuffer = null;
            }
            // 分配取消token
            receiveCancelTokenSource?.Dispose();
            receiveCancelTokenSource = null;
        }

        private CancellationTokenSource receiveCancelTokenSource;
        private bool BlockReceiveError = false;
        private async Task ReceiveLoopAsync()
        {
            CancellationToken token = receiveCancelTokenSource.Token;
            // 重置接收区间
            availableStart = 0;
            availableEnd = 0;
            //
            while (true)
            {
                (T, object) result = await Receive(token);
                invokeHelper.InvokeOnMainThreadPost<(T, object)>(OnReceivedObject, result);
            }
        }
    }
}
