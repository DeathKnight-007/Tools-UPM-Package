using JetBrains.Annotations;
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
using UnityEngine;
using static ENetAPI;

namespace DeathKnight.Net
{
    public class MyEnetClient<M, T, U> : IMyENet‌Client where M : BaseProto, new() where T : VerifyProto, new() where U : IENetSimpleClient, new()
    {
        public IPEndPoint ServerIpEndPoint { get; private set; }

        public int FrameBufferSize { get; private set; }

        public ISerializer Serializer { get; private set; }

        public event Action<(BaseProto, object)> OnReceivedObject;
        public event Action<long> OnReceivedFrame;
        public event Action<Exception> OnReceivedFail;
        public event Action<Exception> OnSendFail;
        public event Action OnConnected;
        public event Action<Exception> OnConnectedFail;
        public event Action OnDisconnected;



        private T t = new();
        private M m = new();
        private InvokeOnMainThreadHelper invokeHelper;
        private bool inited = false;
        public void Init(int frameBufferSize, ISerializer serializer)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ENetSimpleClient));
            if (inited)
                return;
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
            invokeHelper = new();
            inited = true;
            mainENetIdle.Set();
        }

        #region 连接
        enum ConnectedState
        {
            None = 0,
            SendConnected = 1 << 1,
            ReceiveConnected = 1 << 2,
        }

        private Task connectTask, sendTask, receiveTask;
        private CancellationTokenSource connectCancellSource;
        private ConnectedState realConnectedState; // 实际连接状态
        private U activeClient;

        private void ClearConnectedState(ConnectedState state)
        {
            if (realConnectedState.HasFlag(state))
                realConnectedState -= state;
        }

        private void SetConnectedState(ConnectedState state)
        {
            if (!realConnectedState.HasFlag(state))
            {
                realConnectedState = realConnectedState | state;
            }
        }

        private bool CheckCanConnect()
        {
            // 如果当前真实连接状态，不是空状态，则不能连接
            if (realConnectedState != ConnectedState.None)
            {
                return false;
            }
            if (connectting)
                return false;
            // 当连接任务还在进行中，则不能再连接
            if (connectTask != null && !connectTask.IsCompleted)
            {
                return false;
            }
            // 如果发送任务还在进行中，则不能连接
            if (sendTask != null && !sendTask.IsCompleted)
            {
                return false;
            }
            // 如果接收任务还在进行中，则不能连接
            if (receiveTask != null && !receiveTask.IsCompleted)
            {
                return false;
            }

            return true;
        }
        public async Task Connect(IPEndPoint ip)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ENetSimpleClient));
            if (!inited)
                throw new Exception("not inited");
            if (!CheckCanConnect())
            {
                Debug.LogError("can't connect now");
                return;
            }
            // 异步:连接过程，资源控制全部都在异步线程之内
            // 异步之后主线程同步执行: 连接状态设定、事件抛出
            await ConnectHelp(0, ip: ip);
        }
         
        public async Task Connect(string host, int port)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ENetSimpleClient));
            if (!inited)
                throw new Exception("not inited");
            if (!CheckCanConnect())
            {
                Debug.LogError("can't connect now");
                return;
            }
            await ConnectHelp(1, host: host, port: port);
        }

        private bool connectting;
        /// <summary>
        /// type = 0 则用ip方式连接， type = 1 则使用域名方式连接
        /// 异步:连接过程，资源控制
        /// 异步之后主线程同步执行: 连接状态设定、事件抛出
        /// </summary>
        /// <returns></returns>
        private async Task ConnectHelp(int type = 0, IPEndPoint ip = null, string host = "", int port = 0)
        {
            if (!CheckCanConnect())
            {
                Debug.LogError("can't connect now");
                return;
            }
            connectting = true;
            ServerIpEndPoint = null;
            Exception error = null;
            U client = default(U);

            try
            {
                client = new();
                client.Init();
                connectCancellSource = new();
                CancellationToken token = connectCancellSource.Token;

                if (type == 0)
                {
                    connectTask = client.Connect(ip, token);
                    await connectTask; // 真正异步在这里
                }
                else if (type == 1)
                {
                    connectTask = client.Connect(host, port, token);
                    await connectTask; // 真正异步在这里
                }
                else
                {
                    throw new Exception();
                }

                token.ThrowIfCancellationRequested();

                activeClient = client;

                SetConnectedState(ConnectedState.SendConnected);
                SetConnectedState(ConnectedState.ReceiveConnected);

                ServerIpEndPoint = activeClient.ServerIpEndPoint;

                _ = StartSendLoopAsync();
                _ = StartReceiveLoopAsync();
            }
            catch (OperationCanceledException) when (connectCancellSource.Token.IsCancellationRequested)
            {
                client?.Dispose();
                activeClient = default(U);
            }
            catch (Exception e)
            {
                client?.Dispose();
                activeClient = default(U);
                Debug.LogException(e);
                error = e;
            }
            finally
            {
                connectCancellSource?.Dispose();
                connectCancellSource = null;
                connectting = false;
            }
            if(realConnectedState.HasFlag(ConnectedState.SendConnected) && realConnectedState.HasFlag(ConnectedState.ReceiveConnected))
            {
                try
                {
                    OnConnected?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
            if(error != null)
            {
                try
                {
                    OnConnectedFail?.Invoke(error);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
        #endregion

        #region 发送过程

        private ManualResetEventSlim mainENetIdle = new();
        private object enetLock = new();
        private ConcurrentQueue<(BaseProto Header, object Value, ENetPacketFlag Flag)> messageQueue = new();
        private SemaphoreSlim sendSignal;
        public void Send<X>(X data, BaseProto header, ENetPacketFlag flag)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ENetSimpleClient));
            if (!inited)
                throw new Exception("not inited");
            if (!realConnectedState.HasFlag(ConnectedState.SendConnected))
            {
                Debug.LogError($"send has closed:{header.ToString()}");
                return;
            }
            messageQueue.Enqueue((header, data, flag));
            sendSignal.Release();
        }

        private void SendHelp(byte[] buffer, int offset, int count, ENetPacketFlag flag)
        {
            mainENetIdle.Wait(); // 等待enet空闲
            lock (enetLock) // 占住enet
            {
                sendCancelTokenSource.Token.ThrowIfCancellationRequested();
                activeClient.Send(buffer, offset, count, flag);
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
            (BaseProto Header, object Value, ENetPacketFlag Flag) element;
            if (!messageQueue.TryDequeue(out element))
            {
                return;
            }
            BaseProto header = element.Header;
            // 首先校验header类型，以及flag对吗
            if (element.Flag == ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED)
            {
                if (header is not T)
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
                if(header is not M)
                {
                    throw new ArgumentException();
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
                    SendHelp(writeBuffer, 0, count + t.HeaderLength, element.Flag);
                }
                else if (element.Flag == ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE)
                {
                    bytesObjectTransfer.ParseHeaderToBytes(header as M, writeBuffer, 0);
                    SendHelp(writeBuffer, 0, count + m.HeaderLength, element.Flag);
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
                    bytesObjectTransfer.ParseObjectToBytesFragment(target, header as M);
                }

                bool hasNext = true;
                while (hasNext)
                {
                    header.RequestId = requestId;
                    header.Timestamp = Environment.TickCount;// 操作系统启动到现在经过的毫秒数
                    hasNext = bytesObjectTransfer.ParseObjectToBytesFragmentNext(target, header as M, writeBuffer);
                    SendHelp(writeBuffer, 0, header.PayloadLength + m.HeaderLength, element.Flag);
                    requestId++;
                }
            }
        }

        #endregion
        #region 发送循环
        private byte[] writeBuffer; // 发送帧数据缓冲池，64k之上已经可以算是大堆了，使用可以回收byte[]
        private long requestId = 0;
        private CancellationTokenSource sendCancelTokenSource;

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
            // 发送取消token
            sendCancelTokenSource = new();
            // 发送缓冲资源
            messageQueue.Clear();
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
            //
            messageQueue.Clear();
        }
        private async Task StartSendLoopAsync()
        {
            Exception error = null;
            try
            {
                SendLoopAsyncPrepare();
                CancellationToken token = sendCancelTokenSource.Token;
                sendTask = Task.Run(() =>
                {
                    while (true)
                    {
                        // 等待发送信号期间也可以打断循环
                        sendSignal.Wait(token);
                        // 发送间隙可以，打断发送循环
                        if (token.IsCancellationRequested)
                        {
                            throw new OperationCanceledException();
                        }
                        SendQueueOnce(); // 如果主线程token取消之后确保不能再发送数据，但是需要lockenet才能彻底锁死不发送
                    }
                });
                await sendTask;
            }
            catch (OperationCanceledException e)
            {
                Debug.LogException(e);
            }
            catch (Exception e)
            {
                // 发送异常结束
                Debug.LogException(e);
                error = e;
            }
            finally
            {
                SendLoopAsyncDispose();
            }

            ClearConnectedState(ConnectedState.SendConnected);
            bool alldisconnected = CheckAndReleaseSendReceiveCommonRes();

            // 如果还在接收状态则，则发送FIN
            if (realConnectedState.HasFlag(ConnectedState.ReceiveConnected))
                DisconnectFIN();

            // 发送途中报错，抛出系统外异常
            if (error != null)
            {
                try
                {
                    // 触发发送异常回调
                    OnSendFail?.Invoke(error);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
            if (alldisconnected)
            {
                try
                {
                    OnDisconnected?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        #endregion
        #region 接收循环
        private byte[] readBuffer; // 总接收数据缓冲池,，64k之上已经可以算是大堆了，使用可以回收byte[]
        private CancellationTokenSource receiveCancelTokenSource;
        private int maxCache = 32 * 1024 * 1024; // 接收缓冲达到32MB，则开始尽力清理已经消费的缓冲
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
            cacheReceiveSim = new SemaphoreSlim(0);
            //
            mainReceiveIdle.Set();
            //
            cachedReceiveFrame.Clear();
            if (cachedReceiveStream.Capacity < maxCache)
            {
                cachedReceiveStream.Capacity = maxCache;
            }
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
            mainReceiveIdle.Set();
            //
            cachedReceiveFrame.Clear();
            cachedReceiveStream.SetLength(0);
            cachedReceiveStream.Position = 0;
        }

        private bool receiveRuning = false;
        private async Task StartReceiveLoopAsync()
        {
            Exception error = null;
            try
            {
                receiveRuning = true;
                ReceiveLoopAsyncPrepare();
                CancellationToken token = receiveCancelTokenSource.Token;
                receiveTask = Task.Run(() =>
                {
                    while (true)
                    {
                        lock (cacheReceiveLock)
                        {
                            // 如果所有的缓冲消息都处理完了，则清空接收缓存
                            if (cacheReceiveSim.CurrentCount == 0)
                            {
                                cachedReceiveStream.SetLength(0);
                                cachedReceiveStream.Position = 0;
                            }
                            else
                            {
                                // 如果缓冲消息挤压过多，且已经处理过的比较多。 则强行清空已经处理过的缓存，但是这样比较消耗性能
                                int totalLength = (int)cachedReceiveStream.Length;
                                int maxLength = maxCache;
                                int dealedLength = cachedReceiveFrame.Peek().start;
                                float dealedPersent = 0.5f;
                                if (cachedReceiveStream.Length > maxLength && ((float)dealedLength / (float)totalLength) > dealedPersent)
                                {
                                    // 移动cachedReceiveStream
                                    int startPos = dealedLength;
                                    int endPos = totalLength;
                                    byte[] cachedReceiveStreamBuffer = cachedReceiveStream.GetBuffer();
                                    Buffer.BlockCopy(cachedReceiveStreamBuffer, startPos, cachedReceiveStreamBuffer, 0, endPos - startPos);
                                    int remainingLength = endPos - startPos;
                                    cachedReceiveStream.SetLength(remainingLength);
                                    cachedReceiveStream.Position = remainingLength;
                                    // cachedReceiveFrame元素值修正
                                    int count = cachedReceiveFrame.Count;
                                    for (int i = 0; i < count; i++)
                                    {
                                        var frame = cachedReceiveFrame.Dequeue();
                                        frame.start -= startPos;
                                        cachedReceiveFrame.Enqueue(frame);
                                    }
                                }
                            }
                        }
                        cacheReceiveSim.Wait(token);
                        mainReceiveIdle.Wait(token); // 等待主线程使用cache资源结束
                        if (token.IsCancellationRequested)
                        {
                            throw new OperationCanceledException();
                        }
                        lock (cacheReceiveLock)
                        {
                            (BaseProto, object) result;
                            bool success = TryPareseOneReceive(cachedReceiveFrame.Dequeue(), out result);
                            cachedReceiveStream.Position = cachedReceiveStream.Length;// 解析的时候会搞乱Stream的指标，这里恢复一下
                            if (success)
                            {
                                invokeHelper.InvokeOnMainThreadPost<(BaseProto, object)>(OnReceivedObject, result);
                            }
                        }
                    }
                });
                await receiveTask;
            }
            catch (EndOfStreamException e) when(e.Message == "FIN")
            {
                Debug.LogException(e);
            }
            catch (OperationCanceledException e)
            {
                Debug.LogException(e);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e;
            }
            finally
            {
                ReceiveLoopAsyncDispose();
                if (disposed)
                {
                    cachedReceiveStream.SetLength(0);
                    cachedReceiveStream.Capacity = 0;
                    cachedReceiveStream.Dispose();
                }
            }
            ClearConnectedState(ConnectedState.ReceiveConnected);
            bool alldisconnected = CheckAndReleaseSendReceiveCommonRes();
            // 主动断开连接
            if (realConnectedState.HasFlag(ConnectedState.SendConnected))
                sendCancelTokenSource.Cancel();
            receiveRuning = false;
            if (error != null)
            {
                try
                {
                    // 触发发送异常回调
                    OnReceivedFail?.Invoke(error);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
            if (alldisconnected)
            {
                try
                {
                    OnDisconnected?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
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

        private ManualResetEventSlim mainReceiveIdle = new();
        private int maxReceive = 0; // 单帧接收数据上限
        /// <summary>
        /// 将所有接收都处理了, 获取到锁一次全处理完
        /// </summary>
        /// <returns>是否还可以再接收</returns>
        private void Receive()
        {
            if (!realConnectedState.HasFlag(ConnectedState.ReceiveConnected))
            {
                return;
            }
            mainReceiveIdle.Reset();
            mainENetIdle.Reset();
            try
            {
                lock (cacheReceiveLock)
                {
                    lock (enetLock)
                    {
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
                                throw e;
                            }
                        }
                    }
                }
            }
            finally
            {
                mainReceiveIdle.Set();
                mainENetIdle.Set();
            }
        }

        private ParseBytesObject<M> bytesObjectTransfer;
        private ParseBytesObjectVerify<T> bytesObjectTransferVerify;

        /// <summary>
        /// 尝试处理一次接收
        /// </summary>
        /// <param name="target"></param>
        /// <returns>是否处理成功，成功了则item有效</returns>
        private bool TryPareseOneReceive((int start, int count, ENetPacketFlag flag) source, out (BaseProto Header, object Value) item)
        {
            item = new();
            ENetPacketFlag flag = source.flag;
            int count = source.count;
            if (count > 0)
            {
                if (flag == ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED)
                {
                    // 如果是不可靠消息，则必须是可校验的消息头
                    if (source.count > FrameBufferSize)
                    {
                        return false;
                    }
                    cachedReceiveStream.Position = source.start;
                    T header;
                    try
                    {
                        header = bytesObjectTransferVerify.ParseHeaderFromBytes(cachedReceiveStream, source.count);
                    }
                    catch
                    {
                        return false;
                    }
                    bool hashValid = false;
                    try
                    {
                        hashValid = header.ValidHash(cachedReceiveStream);
                    }
                    catch
                    {

                    }
                    if (!hashValid) // 内容出错属于可接受范围，丢弃即可
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
                    if (header.PayloadLength + header.HeaderLength != count)
                    {
                        throw new Exception("header.PayloadLength + header.HeaderLength != count");
                    }
                    item.Header = header as BaseProto;
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
                throw new EndOfStreamException("FIN");
            }
        }

        #endregion

        /// <summary>
        /// 请求终止发送线程，发送FIN,  等待真的完成断开连接（收发线程都终止，连接也彻底断开）
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Disconnect()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ENetSimpleClient));
            if (!inited)
                throw new Exception("not inited");
            // 如果正在连接，则取消连接
            if (connectting)
            {
                connectCancellSource.Cancel();
            }
            else if (realConnectedState.HasFlag(ConnectedState.SendConnected))
            {
                sendCancelTokenSource.Cancel();
            }
        }

        private void DisconnectFIN()
        {

            // 发送FIN
            try
            {
                lock (enetLock)
                {
                    activeClient.Disconnect();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        // 如果发送和接收线程都结束了，则释放一些共同的资源
        private bool CheckAndReleaseSendReceiveCommonRes()
        {
            if (realConnectedState == ConnectedState.None)
            {
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
                return true;
            }
            return false;
        }

        // 进入释放流程
        private bool disposed = false;
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (realConnectedState.HasFlag(ConnectedState.SendConnected))
                DisconnectFIN(); // 发送FIN
            connectCancellSource?.Cancel();
            sendCancelTokenSource?.Cancel();
            receiveCancelTokenSource?.Cancel();
            // 连接任务，发送任务，接收任务结束，都将资源正确回收了。所以释放只要取消这三个线程运行即可
            if(!receiveRuning)
            {
                cachedReceiveStream.SetLength(0);
                cachedReceiveStream.Capacity = 0;
                cachedReceiveStream.Dispose();
            }
        }

        public void Update()
        {
            if (disposed)
                return;
            if (!inited)
                return;
            if (realConnectedState.HasFlag(ConnectedState.ReceiveConnected))
            {
                // 接收所有消息
                Receive();
            }
        }
    }
}
