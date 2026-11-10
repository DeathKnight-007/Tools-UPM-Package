using SerializableReadWrite;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

        private T t = new();
        private InvokeOnMainThreadHelper invokeHelper;
        private bool inited = false;
        public void Init(NetClientConfig config, int frameBufferSize, ISerializer serializer)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ENetSimpleClient));
            if (inited)
                return;
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
            inited = true;
        }


        #region 连接
        enum ConnectedState
        {
            None = 0,
            SendConnected = 1 << 1,
            ReceiveConnected = 1 << 2,
        }

        private Task connectTask, sendTask, receiveTask;
        private ConnectedState realConnectedState = ConnectedState.None; // 实际连接状态
        private CancellationTokenSource connectCancellSource;
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
            // 异步:连接过程，资源控制全部都在异步线程之内
            // 异步之后主线程同步执行: 连接状态设定、事件抛出
            await ConnectHelp(1, host: host, port:port);
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
                activeClient = client;
                client.Init(Config);
                connectCancellSource = new();
                CancellationToken token = connectCancellSource.Token;

                try
                {
                    if (type == 0)
                    {
                        connectTask = client.Connect(ip);
                        await connectTask; // 真正异步在这里
                    }
                    else if (type == 1)
                    {
                        connectTask = client.Connect(host, port);
                        await connectTask; // 真正异步在这里
                    }
                    else
                    {
                        throw new Exception();
                    }
                }catch(Exception) when(token.IsCancellationRequested){
                    token.ThrowIfCancellationRequested();// 使用cancel + client.close 强行终止连接，并抛出取消事件
                }

                token.ThrowIfCancellationRequested();

                SetConnectedState(ConnectedState.SendConnected);
                SetConnectedState(ConnectedState.ReceiveConnected);

                ServerIpEndPoint = activeClient.ServerIpEndPoint;
                ClientIPEndPoint = activeClient.ClientIPEndPoint;

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
            if (realConnectedState.HasFlag(ConnectedState.SendConnected) && realConnectedState.HasFlag(ConnectedState.ReceiveConnected))
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
            if (error != null)
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
        private byte[] writeBuffer; // 发送帧数据缓冲池，64k之上已经可以算是大堆了，使用可以回收byte[]
        private long requestId = 0;
        private ConcurrentQueue<(T Header, object Value)> messageQueue = new();
        private SemaphoreSlim sendSignal;
        private CancellationTokenSource sendCancelTokenSource;
        public void Send<M>(M data, T header)
        {
            if (realConnectedState.HasFlag(ConnectedState.SendConnected))
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
                token.ThrowIfCancellationRequested();
                if (count > FrameBufferSize - t.HeaderLength)
                {
                    throw new InvalidDataException("Payload exceeds frame size; enable fragmentation.");
                }
                //写入消息头
                header.RequestId = requestId;
                header.Timestamp = Environment.TickCount;// 操作系统启动到现在经过的毫秒数
                header.PayloadLength = count;
                bytesObjectTransfer.ParseHeaderToBytes(header, writeBuffer, 0);
                token.ThrowIfCancellationRequested();
                await activeClient.Send(writeBuffer, 0, count + t.HeaderLength, token);
                requestId++;
            }
            else
            {
                bytesObjectTransfer.ParseObjectToBytesFragment(target, header);
                token.ThrowIfCancellationRequested();
                bool hasNext = true;
                while (hasNext)
                {
                    header.RequestId = requestId;
                    header.Timestamp = Environment.TickCount;// 操作系统启动到现在经过的毫秒数
                    hasNext = bytesObjectTransfer.ParseObjectToBytesFragmentNext(target, header, writeBuffer);
                    token.ThrowIfCancellationRequested();
                    await activeClient.Send(writeBuffer, 0, header.PayloadLength + t.HeaderLength, token);
                    requestId++;
                }
            }
            token.ThrowIfCancellationRequested();
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
                sendTask = Task.Run(async () =>
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
                        await SendQueueOnce(token); // 如果主线程token取消之后确保不能再发送数据，但是需要lockenet才能彻底锁死不发送
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
        #region 接收过程
        private byte[] readBuffer; // 总接收数据缓冲池,，64k之上已经可以算是大堆了，使用可以回收byte[]
        private int availableStart = 0, availableEnd = 0;
        private ParseBytesObject<T> bytesObjectTransfer;
        private CancellationTokenSource receiveCancelTokenSource;
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
                throw new EndOfStreamException("FIN");
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
                        throw new InvalidDataException( $"Invalid payload length: {item.Header.PayloadLength}");
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
                // 保存分片的第一个消息头
                T firstheader = item.Header;
                T header = item.Header;
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
                            item.Header = firstheader;
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

        private bool receiveRuning = false;
        private async Task StartReceiveLoopAsync()
        {
            Exception error = null;
            try
            {
                receiveRuning = true;
                ReceiveLoopAsyncPrepare();
                CancellationToken token = receiveCancelTokenSource.Token;
                receiveTask = Task.Run(async () =>
                {
                    // 重置接收区间
                    availableStart = 0;
                    availableEnd = 0;
                    //
                    while (true)
                    {
                        (T, object) result = await Receive(token);
                        invokeHelper.InvokeOnMainThreadPost<(T, object)>(OnReceivedObject, result);
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

        private void DisconnectFIN()
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

        private bool CheckAndReleaseSendReceiveCommonRes()
        {
            if (realConnectedState == ConnectedState.None)
            {
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
                return true;
            }
            return false;
        }

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
                activeClient?.Dispose();
            }
            else if (realConnectedState.HasFlag(ConnectedState.SendConnected))
            {
                sendCancelTokenSource.Cancel();
            }
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

            // 如果正在连接，则取消连接
            if (connectting)
            {
                connectCancellSource.Cancel();
                activeClient?.Dispose();
            }
            sendCancelTokenSource?.Cancel();
            receiveCancelTokenSource?.Cancel();
        }
    }
}
