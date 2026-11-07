using SerializableReadWrite;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEngine;
using static ENetAPI;
using static Unity.IO.LowLevel.Unsafe.AsyncReadManagerMetrics;

namespace DeathKnight.Net
{
    public class MyEnetClient<M, T, U> : IMyENet‌Client<M> where M : BaseProto, new() where T : VerifyProto, new() where U : IENetSimpleClient, new()
    {
        public IPEndPoint ServerIpEndPoint { get; private set; }

        public int FrameBufferSize { get; private set; }

        public ISerializer Serializer { get; private set; }

        public NetClientConfig Config { get; private set; }

        public event Action<(M, object)> OnReceivedObject;
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
        private M m = new();
        private U activeClient;
        private InvokeOnMainThreadHelper invokeHelper;
        public void Init(NetClientConfig config, int frameBufferSize, ISerializer serializer)
        {
            if (frameBufferSize <= t.HeaderLength || frameBufferSize <= m.HeaderLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameBufferSize),
                    frameBufferSize,
                    $"Frame buffer size must be greater than the header length ({t.HeaderLength}, {m.HeaderLength}).");
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

        enum ConnectedState
        {
            None = 0,
            SendConnected = 1 << 1,
            ReceiveConnected = 1 << 2,
        }
        #region 连接
        private Task connectTask; // 连接任务
        private ConnectedState realConnectedState = ConnectedState.None; // 实际连接状态
        private ConnectedState requestConnectedState = ConnectedState.None; // 请求连接状态，实际可能还没有成这个状态呢

        private bool CheckCanConnect()
        {
            // 当前连接状态，如果还处于连接中，则不能再连接
            if (realConnectedState != ConnectedState.None || requestConnectedState != ConnectedState.None)
            {
                return false;
            }
            // 当连接任务还在进行中，则不能再连接
            if (connectTask != null && !connectTask.IsCompleted)
            {
                return false;
            }
            // 如果发送任务还在进行中，则不能连接
            if (sendTask != null && !sendTask.IsCompleted)
                return false;
            // 如果接收任务还在进行中，则不能连接
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

            ServerIpEndPoint = null;

            U client = new();
            client.Init(Config);
            Exception failEx = null;
            try
            {
                connectCancellSource = new();
                CancellationToken token = connectCancellSource.Token;
                activeClient = client;
                await ConnectCore(client.Connect(ip, token));
            }
            catch (OperationCanceledException e)when(connectCancellSource.Token.IsCancellationRequested)
            {
                client?.Dispose();
                activeClient = default(U);
                failEx = e;
            }
            catch (Exception e)
            {
                client?.Dispose();
                activeClient = default(U);
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
            if(failEx != null && realConnectedState == ConnectedState.None)
                invokeHelper.InvokeOnMainThreadPost<Exception>((ex) => { OnConnectedFail?.Invoke(ex); }, failEx);
        }

        public async Task Connect(string host, int port)
        {
            if (!CheckCanConnect())
            {
                Debug.LogError("can't connect now");
                return;
            }

            ServerIpEndPoint = null;

            U client = new();
            client.Init(Config);
            Exception failEx = null;
            try
            {
                connectCancellSource = new();
                CancellationToken token = connectCancellSource.Token;
                activeClient = client;
                await ConnectCore(client.Connect(host, port, token));
            }
            catch (OperationCanceledException e) when (connectCancellSource.Token.IsCancellationRequested)
            {
                client?.Dispose();
                activeClient = default(U);
                connectProgress = Progress.Completed;
            }
            catch (Exception e)
            {
                client?.Dispose();
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
        private async Task ConnectCore(Task connect)
        {
            connectBlockError = false;
            await connect;

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

            ServerIpEndPoint = activeClient.ServerIpEndPoint;

            sendTask = StartSendLoopAsync();
            receiveTask = StartReceiveLoopAsync();

            invokeHelper.InvokeOnMainThreadPost(OnConnected);
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
                bytesObjectTransfer = new ParseBytesObject<M>(Serializer, FrameBufferSize);
            if (bytesObjectTransferVerify == null)
                bytesObjectTransferVerify = new ParseBytesObjectVerify<T>(Serializer, FrameBufferSize);
            // 取消token
            sendCancelTokenSource = new();
            //
            BlockSendError = false;
            mainSendNotWorking.Set();
            //
            messageQueue.Clear();
            cachedSendFrame.Clear();
            cachedSendStream.SetLength(0);
            cachedSendStream.Position = 0;
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
            mainSendNotWorking.Set();

            messageQueue.Clear();
            cachedSendFrame.Clear();
            cachedSendStream.SetLength(0);
            cachedSendStream.Position = 0;
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
            mainSendNotWorking.Reset();
            lock (cacheSendLock)
            {
                cachedSendFrame.Clear();
                cachedSendStream.SetLength(0);
                cachedSendStream.Position = 0;
            }
            mainSendNotWorking.Set();
            return true;
        }


        #endregion
        #region 发送过程

        private MemoryStream cachedSendStream = new();
        private Queue<(int start, int count, ENetPacketFlag flag)> cachedSendFrame = new();
        private void CacheSend(byte[] buffer, int offset, int count, ENetPacketFlag flag)
        {
            mainSendNotWorking.Wait(sendCancelTokenSource.Token);
            lock (cacheSendLock)
            {
                int start = (int)cachedSendStream.Position;
                cachedSendStream.Write(buffer, offset, count);
                cachedSendFrame.Enqueue((start, count, flag));
            }
        }
        private object cacheSendLock = new();
        private ManualResetEventSlim mainSendNotWorking = new();
        private void FlushCached()
        {
            mainSendNotWorking.Reset();
            lock (cacheSendLock)
            {
                try
                {
                    if (activeClient != null)
                    {
                        if (realConnectedState.HasFlag(ConnectedState.SendConnected) && requestConnectedState.HasFlag(ConnectedState.SendConnected))
                        {
                            (int start, int count, ENetPacketFlag flag) frame;
                            while (cachedSendFrame.TryDequeue(out frame))
                            {
                                cachedSendStream.Position = frame.start;
                                activeClient.Send(cachedSendStream, frame.count, frame.flag);
                            }
                            cachedSendStream.Position = 0;
                            cachedSendStream.SetLength(0);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (!BlockSendError)
                    {
                        Debug.LogException(e);
                        try
                        {
                            OnSendFail?.Invoke(e);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogException(ex);
                        }
                        Disconnect();
                    }
                    throw e;
                }
                finally
                {
                    mainSendNotWorking.Set();
                    cachedSendStream.Position = 0;
                    cachedSendStream.SetLength(0);
                }
            }
        }

        /// <summary>
        /// 发送队列里的一个消息
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private void SendQueueOnce()
        {
            (M Header, object Value, ENetPacketFlag Flag) element;
            if (!messageQueue.TryDequeue(out element))
            {
                return;
            }
            M header = element.Header;
            // 首先校验header类型，以及flag对吗
            if (element.Flag == ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED)
            {
                if(header is not T)
                {
                    throw new ArgumentException("不可靠消息必须使用校验proto");
                }
            }
            else if (element.Flag == ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE)
            {
                if (header is T)
                {
                    throw new ArgumentException("可靠消息必须使用非校验proto");
                }
            }
            else
            {
                throw new Exception("not support ENetPacketFlag:" + element.Flag);
            }
            object target = element.Value;
            if (!header.FragmentInfo.NeedFragment)
            {
                int count = 0;
                if (element.Flag == ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED)
                {
                    count = bytesObjectTransferVerify.ParseObjectToBytes(target, writeBuffer, t.HeaderLength);
                    if (count > FrameBufferSize - t.HeaderLength)
                    {
                        throw new InvalidDataException("Payload exceeds frame size; enable fragmentation.");
                    }
                }
                else if (element.Flag == ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE)
                {
                    count = bytesObjectTransfer.ParseObjectToBytes(target, writeBuffer, m.HeaderLength);
                    if (count > FrameBufferSize - m.HeaderLength)
                    {
                        throw new InvalidDataException("Payload exceeds frame size; enable fragmentation.");
                    }
                }

                //写入消息头
                header.RequestId = requestId;
                header.Timestamp = Environment.TickCount;// 操作系统启动到现在经过的毫秒数
                header.PayloadLength = count;

                if (element.Flag == ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED)
                {
                    bytesObjectTransferVerify.ParseHeaderToBytes(header as T, writeBuffer, 0);
                    bytesObjectTransferVerify.ComputeHash(header as T, writeBuffer, 0);
                    CacheSend(writeBuffer, 0, count + t.HeaderLength, element.Flag);
                }
                else if (element.Flag == ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE)
                {
                    bytesObjectTransfer.ParseHeaderToBytes(header, writeBuffer, 0);
                    CacheSend(writeBuffer, 0, count + m.HeaderLength, element.Flag);
                }
                requestId++;
            }
            else
            {
                if (element.Flag == ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED)
                {
                    throw new Exception("fragement is not support ENET_PACKET_FLAG_UNSEQUENCED");
                }
                else if (element.Flag == ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE)
                {
                    bytesObjectTransfer.ParseObjectToBytesFragment(target, header);
                }
 
                bool hasNext = true;
                while (hasNext)
                {
                    header.RequestId = requestId;
                    header.Timestamp = Environment.TickCount;// 操作系统启动到现在经过的毫秒数
                    hasNext = bytesObjectTransfer.ParseObjectToBytesFragmentNext(target, header, writeBuffer);
                    CacheSend(writeBuffer, 0, header.PayloadLength + m.HeaderLength, element.Flag);
                    requestId++;
                }
            }
        }

        private bool BlockSendError = false;
        private CancellationTokenSource sendCancelTokenSource;
        private void SendLoopAsync()
        {
            CancellationToken token = sendCancelTokenSource.Token;
            while (true)
            {
                sendSignal.Wait(token);
                if (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }
                SendQueueOnce();
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
                bytesObjectTransfer = new ParseBytesObject<M>(Serializer, FrameBufferSize);
            }
            if (bytesObjectTransferVerify == null)
            {
                bytesObjectTransferVerify = new ParseBytesObjectVerify<T>(Serializer, FrameBufferSize);
            }
            //
            BlockReceiveError = false;
            //
            cacheReceiveSim = new SemaphoreSlim(0);
            //
            mainReceiveNotWorking.Set();
            //
            cachedReceiveFrame.Clear();
            cachedReceiveStream.SetLength(0);
            cachedReceiveStream.Position = 0;
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
            cacheReceiveSim?.Dispose();
            cacheReceiveSim = null;
            mainReceiveNotWorking.Set();
            //
            cachedReceiveFrame.Clear();
            cachedReceiveStream.SetLength(0);
            cachedReceiveStream.Position = 0;
        }

        private async Task StartReceiveLoopAsync()
        {
            ReceiveLoopAsyncPrepare();
            CancellationToken token = receiveCancelTokenSource.Token;
            /* 结束方式有哪些
                1、异常报错
                2、Dispose导致的异常报错报错
                3、接收异常报错，改为主动请求取消
                4、Dispose 主动请求取消
             */
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
        #region 接收过程

        private MemoryStream cachedReceiveStream = new();
        private Queue<(int start, int count, ENetPacketFlag flag)> cachedReceiveFrame = new();
        private SemaphoreSlim cacheReceiveSim;
        private object cacheReceiveLock = new();
        private void CacheReceive(byte[] buffer, int offset, int count, ENetPacketFlag flag)
        {
            int start = (int)cachedReceiveStream.Position;
            cachedReceiveStream.Write(buffer, offset, count);
            cachedReceiveFrame.Enqueue((start, count, flag));
            cacheReceiveSim.Release();
        }

        private ManualResetEventSlim mainReceiveNotWorking = new();
        /// <summary>
        /// 将所有接收都处理了, 获取到锁一次全处理完
        /// </summary>
        /// <returns>是否还可以再接收</returns>
        private void Receive()
        {
            mainReceiveNotWorking.Reset();
            try
            {
                lock (cacheReceiveLock)
                {
                    if (activeClient == null)
                        return;
                    if (realConnectedState.HasFlag(ConnectedState.ReceiveConnected) && requestConnectedState.HasFlag(ConnectedState.ReceiveConnected))
                    {

                    }
                    else
                    {
                        return;
                    }
                    while (true)
                    {
                        try
                        {
                            ENetPacketFlag flag;
                            int count = activeClient.Receive(readBuffer, 0, FrameBufferSize, out flag);
                            if (count > 0)
                            {
                                CacheReceive(readBuffer, 0, count, flag);
                            }
                            else
                            {
                                break;
                            }
                        }
                        catch (EndOfStreamException)
                        {
                            // 服务器发来FIN, 将数据长度为0，定义为FIN
                            CacheReceive(readBuffer, 0, 0, default(ENetPacketFlag));
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
                                receiveCancelTokenSource?.Cancel();
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
                            throw e;
                        }
                    }
                }
            }
            finally
            {
                mainReceiveNotWorking.Set();
            }
        }

        private ParseBytesObject<M> bytesObjectTransfer;
        private ParseBytesObjectVerify<T> bytesObjectTransferVerify;

        /// <summary>
        /// 尝试处理一次接收
        /// </summary>
        /// <param name="target"></param>
        /// <returns>是否处理成功，成功了则item有效</returns>
        private bool TryPareseOneReceive((int start, int count, ENetPacketFlag flag) source, out (M Header, object Value) item)
        {
            item = new();
            ENetPacketFlag flag = source.flag;
            int count = source.count;
            if (count > 0)
            {
                if (flag == ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED)
                {
                    // 如果是不可靠消息，则必须是可校验的消息头
                    cachedReceiveStream.Position = source.start;
                    T header = bytesObjectTransferVerify.ParseHeaderFromBytes(cachedReceiveStream, source.count);
                    if (!header.ValidHash(cachedReceiveStream)) // 内容出错属于可接受范围，丢弃即可
                    {
                        return false;
                    }
                    if (!header.Valid()) // 内容没有出错，但是header版本等信息出错，则不可接受
                    {
                        throw new Exception("header is not valid:" + header);
                    }
                    if (header.PayloadLength > FrameBufferSize - t.HeaderLength || header.PayloadLength < 0)
                    {
                        throw new InvalidDataException($"Invalid payload length: {header.PayloadLength}");
                    }
                    if(header.PayloadLength + header.HeaderLength != count)
                    {
                        throw new Exception("header.PayloadLength + header.HeaderLength != count");
                    }
                    item.Header = header as M;
                    if (!item.Header.FragmentInfo.NeedFragment)
                    {
                        cachedReceiveStream.Position = source.start + header.HeaderLength;
                        item.Value = bytesObjectTransferVerify.ParseObjectFromBytes(header, cachedReceiveStream, header.PayloadLength);
                        invokeHelper.InvokeOnMainThreadPost<long>(OnReceivedFrame, item.Header.RequestId);
                        return true;
                    }
                    else
                    {
                        throw new Exception("fragement is not support ENET_PACKET_FLAG_UNSEQUENCED");
                    }
                }
                else if (flag == ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE)
                {
                    // 如果是可靠消息，则必须是非校验的消息头
                    cachedReceiveStream.Position = source.start;
                    M header = bytesObjectTransfer.ParseHeaderFromBytes(cachedReceiveStream, source.count);
                    if (!header.Valid()) // header版本等信息出错，不可接受
                    {
                        throw new Exception("header error:" + header.ToString());
                    }
                    if (header.PayloadLength > FrameBufferSize - m.HeaderLength || header.PayloadLength < 0)
                    {
                        throw new InvalidDataException($"Invalid payload length: {header.PayloadLength}");
                    }
                    if (header.PayloadLength + header.HeaderLength != count)
                    {
                        throw new Exception("header.PayloadLength + header.HeaderLength != count");
                    }
                    item.Header = header;
                    if (!header.FragmentInfo.NeedFragment)
                    {
                        cachedReceiveStream.Position = source.start + header.HeaderLength;
                        item.Value = bytesObjectTransfer.ParseObjectFromBytes(header, cachedReceiveStream, header.PayloadLength);
                        invokeHelper.InvokeOnMainThreadPost<long>(OnReceivedFrame, header.RequestId);
                        return true;
                    }
                    else
                    {
                        // 解析消息体
                        cachedReceiveStream.Position = source.start + header.HeaderLength;
                        object result;
                        bool finish = bytesObjectTransfer.ParseObjectFromBytesFragment(header, cachedReceiveStream, header.PayloadLength, out result);
                        invokeHelper.InvokeOnMainThreadPost<long>(OnReceivedFrame, header.RequestId);
                        if (finish)
                        {
                            item.Value = result;
                            return true;
                        }
                        return false;
                    }
                }
                else
                {
                    throw new Exception("not support ENetPacketFlag:" + flag);
                }
            }
            else
            {
                throw new EndOfStreamException();
            }
        }

        private CancellationTokenSource receiveCancelTokenSource;
        private bool BlockReceiveError = false;
        private void ReceiveLoopAsync()
        {
            CancellationToken token = receiveCancelTokenSource.Token;
            while (true)
            {
                // 如果所有的缓冲消息都处理完了，则清空接收缓存
                lock (cacheReceiveLock)
                {
                    if (cacheReceiveSim.CurrentCount == 0)
                    {
                        cachedReceiveStream.SetLength(0);
                        cachedReceiveStream.Position = 0;
                    }
                }
                cacheReceiveSim.Wait(token);
                mainReceiveNotWorking.Wait(token); //
                if (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }
                lock (cacheReceiveLock)
                {
                    (M, object) result;
                    bool success = TryPareseOneReceive(cachedReceiveFrame.Dequeue(), out result);
                    cachedReceiveStream.Position = cachedReceiveStream.Length;// 解析的时候会搞乱Stream的指标，这里恢复一下
                    if (success)
                    {
                        invokeHelper.InvokeOnMainThreadPost<(M, object)>(OnReceivedObject, result);
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// 请求终止发送线程，发送FIN,  等待真的完成断开连接（收发线程都终止，连接也彻底断开）
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Disconnect()
        {
            if(connectProgress == Progress.Doing)
            {
                connectCancellSource?.Cancel();// 这里调用取消后，连接不可能完成，完成后回到主线程，还是将其清空
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
                bytesObjectTransferVerify?.Dispose();
                bytesObjectTransferVerify = null;
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

        private bool disposed = false;
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            _ = SyncDispose();
        }
        private async Task SyncDispose()
        {
            // 如果有正在连接的情况，终止正在连接过程
            if (connectCancellSource != null)
            {
                connectCancellSource?.Cancel();
                await 
            }
            // 请求终止发送异步操作，保证能够终止发送线程
            BlockSendError = true;
            BlockReceiveError = true;
            sendSignal?.Release();
            sendCancelTokenSource?.Cancel();
            receiveCancelTokenSource?.Cancel();
            bytesObjectTransfer?.Dispose();
            bytesObjectTransfer = null;
            bytesObjectTransferVerify?.Dispose();
            bytesObjectTransferVerify = null;
            messageQueue.Clear();
            if (activeClient != null)
            {
                connectBlockError = true;
                try
                {
                    activeClient?.Dispose();
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
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            realConnectedState = ConnectedState.None;
            requestConnectedState = ConnectedState.None;
        }

        private ConcurrentQueue<(M Header, object Value, ENetPacketFlag Flag)> messageQueue = new();
        private SemaphoreSlim sendSignal;
        public void Send<X>(X data, M header, ENetPacketFlag flag)
        {
            if (!realConnectedState.HasFlag(ConnectedState.SendConnected) || !requestConnectedState.HasFlag(ConnectedState.SendConnected))
            {
                Debug.LogError($"send has closed:{header.ToString()}");
                return;
            }
            messageQueue.Enqueue((header, data, flag));
            sendSignal.Release();
        }

        public void Update()
        {
            // Flush序列化完的所有发送消息
            FlushCached();

            // 接收所有消息
            Receive();
        }
    }
}
