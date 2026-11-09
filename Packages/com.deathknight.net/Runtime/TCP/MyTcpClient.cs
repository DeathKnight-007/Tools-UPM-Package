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
    public class MyTcpClient<T, U> : IMyTcpClient<T> where T : BaseProto, new() where U : class, NetSimpleClient, new()
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
        #region 连接
        private Progress connectProgress = Progress.None;
        private ConnectedState realConnectedState = ConnectedState.None; // 实际连接状态
        private ConnectedState requestConnectedState = ConnectedState.None; // 请求连接状态，实际可能还没有成这个状态呢

        private bool CheckCanConnect()
        {
            if (realConnectedState != ConnectedState.None)
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
        private CancellationTokenSource connectCancellSource;
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
            Exception failEx = null;
            try
            {
                connectCancellSource = new();
                CancellationToken token = connectCancellSource.Token;
                Func<Task> connect = () =>
                {
                    return client.Connect(ip);
                };
                activeClient = client;
                await ConnectCore(connect);
            }
            catch (OperationCanceledException e) when (connectCancellSource.Token.IsCancellationRequested)
            {
                client.Dispose();
                activeClient = default(U);
                connectProgress = Progress.Completed;
                failEx = e;
            }
            catch (Exception e)
            {
                client.Dispose();
                activeClient = default(U);
                connectProgress = Progress.Error;
                if (!connectBlockError)
                {
                    Debug.LogException(e);
                }
                failEx = e;
            }
            finally
            {
                connectCancellSource?.Dispose();
                connectCancellSource = null;
            }
            if (failEx != null && realConnectedState == ConnectedState.None)
                invokeHelper.InvokeOnMainThreadPost<Exception>((ex) => { OnConnectedFail?.Invoke(ex); }, failEx);
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
            Exception failEx = null;
            try
            {
                connectCancellSource = new();
                CancellationToken token = connectCancellSource.Token;
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
                activeClient = default(U);
                connectProgress = Progress.Error;
                if (!connectBlockError)
                {
                    Debug.LogException(e);
                    failEx = e;
                }
            }
            finally
            {
                connectCancellSource?.Dispose();
                connectCancellSource = null;
            }
            if (failEx != null && realConnectedState == ConnectedState.None)
                invokeHelper.InvokeOnMainThreadPost<Exception>((ex) => { OnConnectedFail?.Invoke(ex); }, failEx);
        }

        private Task sendTask, receiveTask;
        private async Task ConnectCore(Func<Task> connect)
        {
            connectBlockError = false;
            connectProgress = Progress.Doing;
            await connect();
            connectProgress = Progress.Completed;

            if (connectCancellSource.Token.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }

            if (activeClient == null)
            {
                return;
            }

            realConnectedState = ConnectedState.SendConnected | ConnectedState.ReceiveConnected;
            requestConnectedState = ConnectedState.SendConnected | ConnectedState.ReceiveConnected;

            ClientIPEndPoint = activeClient.ClientIPEndPoint;
            ServerIpEndPoint = activeClient.ServerIpEndPoint;

            sendTask = StartSendLoopAsync();
            receiveTask = StartReceiveLoopAsync();

            invokeHelper.InvokeOnMainThreadPost(OnConnected);
        }
        #endregion
        #region 发送过程
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
        #endregion
        #region 发送循环
        private void SendLoopAsyncPrepare()
        {
            // 写入缓冲
            writeBuffer = ArrayPool<byte>.Shared.Rent(FrameBufferSize);
            // 发送信号
            sendSignal = new SemaphoreSlim(0);
            // 对象序列化工具
            if (bytesObjectTransfer == null)
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

        private async Task StartSendLoopAsync()
        {
            SendLoopAsyncPrepare();
            CancellationToken token = sendCancelTokenSource.Token;
            try
            {
                await Task.Run(() => SendLoopAsync());
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                Debug.LogWarning("send canceled");
                Disconnect();
            }
            catch (Exception e)
            {
                if (!BlockSendError)
                {
                    // 发送异常结束
                    Debug.LogException(e);
                    try
                    {
                        // 触发发送异常回调
                        OnSendFail?.Invoke(e);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                    Disconnect();
                }
            }
            SendLoopAsyncDispose();
        }

        /// <summary>
        /// 请求结束发送循环
        /// </summary>
        /// <returns>是否正常关闭</returns>
        private bool CloseSend()
        {
            // 如果根本没有连接，则不用关闭发送
            if (realConnectedState.HasFlag(ConnectedState.SendConnected))
            {
                // 如果已经请求断开连接了，则不用关闭发送
                if (requestConnectedState.HasFlag(ConnectedState.SendConnected))
                {
                    requestConnectedState -= ConnectedState.SendConnected;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
            messageQueue.Clear();
            sendCancelTokenSource?.Cancel();
            return true;
        }
        #endregion
        #region 接收过程
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

                    if (!header.Valid())
                    {
                        throw new Exception("header not valid:" + header);
                    }
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

                        bool finish = bytesObjectTransfer.ParseObjectFromBytesFragmentNext(header, readBuffer, availableStart + t.HeaderLength, header.PayloadLength);
                        invokeHelper.InvokeOnMainThreadPost<long>(OnReceivedFrame, header.RequestId);
                        availableStart += t.HeaderLength + header.PayloadLength;
                        if (finish)
                        {
                            object result = bytesObjectTransfer.ParseObjectFromBytesFragmentFinal(header);
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
        #endregion
        #region 接收循环

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

        private async Task StartReceiveLoopAsync()
        {
            ReceiveLoopAsyncPrepare();
            CancellationToken token = receiveCancelTokenSource.Token;
            try
            {
                await Task.Run(() => ReceiveLoopAsync());
            }
            catch (EndOfStreamException)
            {
                if (realConnectedState.HasFlag(ConnectedState.ReceiveConnected))
                {
                    // 如果已经请求断开连接了，则不用关闭接收
                    if (requestConnectedState.HasFlag(ConnectedState.ReceiveConnected))
                    {
                        requestConnectedState -= ConnectedState.ReceiveConnected;
                    }
                }
                if (requestConnectedState == ConnectedState.None)
                {
                    _ = OnDisconnectedHelp();
                }
                else
                {
                    Disconnect();
                }
            }
            catch (Exception e)
            {
                if (!BlockReceiveError)
                {
                    Debug.LogException(e);
                    try
                    {
                        OnReceivedFail?.Invoke(e);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                    if (realConnectedState.HasFlag(ConnectedState.ReceiveConnected))
                    {
                        // 如果已经请求断开连接了，则不用关闭接收
                        if (requestConnectedState.HasFlag(ConnectedState.ReceiveConnected))
                        {
                            requestConnectedState -= ConnectedState.ReceiveConnected;
                        }
                    }
                    if (requestConnectedState == ConnectedState.None)
                    {
                        _ = OnDisconnectedHelp();
                    }
                    else
                    {
                        Disconnect();
                    }
                }
                // 强行dispose的就不用发送FIN了
            }
            ReceiveLoopAsyncDispose();
        }
        #endregion

        /// <summary>
        /// 请求终止发送线程，发送FIN,  等待真的完成断开连接（收发线程都终止，连接也彻底断开）
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Disconnect()
        {
            //还正在连接
            if(connectProgress == Progress.Doing)
            {
                Debug.LogError("net is connecting, can't disconnect");
                return;
            }
            if (CloseSend())
            {
                // 发送FIN
                try
                {
                    activeClient.Disconnect();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            // 如果双端都请求了断开连接，则开始等待真的断开连接进行
            if (requestConnectedState == ConnectedState.None)
            {
                if (realConnectedState != ConnectedState.None)
                    _ = OnDisconnectedHelp();
            }
        }

        private bool disconnectedHelpDoing;
        private async Task OnDisconnectedHelp()
        {
            if (realConnectedState == ConnectedState.None)
                return;
            if (disconnectedHelpDoing)
                return;
            disconnectedHelpDoing = true;
            await Task.WhenAll(sendTask, receiveTask);
            try
            {
                bytesObjectTransfer?.Dispose();
                bytesObjectTransfer = null;
                activeClient?.Dispose();
                activeClient = default(U);

            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                realConnectedState = ConnectedState.None;
                disconnectedHelpDoing = false;
            }
            try
            {
                OnDisconnected?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        public void Dispose()
        {
            // 如果有正在连接的情况，终止正在连接过程
            connectCancellSource?.Cancel();
            // 请求终止发送异步操作，保证能够终止发送线程
            BlockSendError = true;
            BlockReceiveError = true;
            messageQueue.Clear();
            bytesObjectTransfer?.Dispose();
            bytesObjectTransfer = null;
            sendSignal?.Release();
            sendCancelTokenSource?.Cancel();
            receiveCancelTokenSource?.Cancel();
            if (activeClient != null)
            {
                connectBlockError = true;
                try
                {
                    activeClient.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                activeClient = default(U);
            }
            if (realConnectedState != ConnectedState.None)
            {
                try
                {
                    OnDisconnected?.Invoke();
                }catch(Exception e)
                {
                    Debug.LogException(e);
                }
            }
            realConnectedState = ConnectedState.None;
            requestConnectedState = ConnectedState.None;
        }

        private ConcurrentQueue<(T Header, object Value)> messageQueue = new();
        private SemaphoreSlim sendSignal;
        public void Send<M>(M data, T header)
        {
            if (realConnectedState.HasFlag(ConnectedState.SendConnected) && requestConnectedState.HasFlag(ConnectedState.SendConnected))
            {
                messageQueue.Enqueue((header, data));
                sendSignal.Release();
            }
            else
            {
                Debug.LogError($"send has closed:{header.ToString()}");
                return;
            }

        }
    }
}
