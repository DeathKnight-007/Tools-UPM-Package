using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using SerializableReadWrite;
using UnityEditor.PackageManager.Requests;
using Unity.VisualScripting.YamlDotNet.Core.Tokens;
using Unity.VisualScripting.Antlr3.Runtime;

namespace DeathKnight.Net
{
    public class TCPNetProtoNetClient : IDisposable
    {

        public int FrameBufferSize { get; }

        private NetClient netClient;
        private byte[] writeBuffer; // 发送帧数据缓冲池
        private byte[] readBuffer; // 接收帧数据缓冲池
        private ConcurrentBag<MemoryStream> bigBuffers; // 大块帧数据缓存池
        private ConcurrentDictionary<ulong, MemoryStream> bigFrames; // 大块帧数据
        private ISerializer serializer;
        private ulong requestId;
        private static object locker = new object();
        public TCPNetProtoNetClient(int frameBufferSize, NetClient netClient, ISerializer serializer)
        {
            this.netClient = netClient;
            this.FrameBufferSize = frameBufferSize;
            readBuffer = new byte[FrameBufferSize];
            writeBuffer = new byte[FrameBufferSize];
            this.serializer = serializer;
            requestId = 0;
            bigBuffers = new();
            bigFrames = new();
            sendCancelTokenSource = new();
            sendTask = Task.Run(SendLoopAsync);
        }
        public void Dispose()
        {
            sendCancelTokenSource.Cancel();
            this.netClient?.Dispose();
        }

        private ConcurrentQueue<(object, TCPNetHeaderProto)> messageQueue = new();
        private Task sendTask;

        /// <summary>
        /// 不支持多线程发送
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="header"></param>
        public void Send<T>(T value, TCPNetHeaderProto header)
        {
            messageQueue.Enqueue((value, header));
            sendSignal.Release();
        }

        private async Task SendQueue(CancellationToken token)
        {
            if (netClient == null)
            {
                throw new InvalidOperationException(
                    "net client not initialized or initialization failed");
            }

            (object, TCPNetHeaderProto) element;
            if (!messageQueue.TryDequeue(out element))
            {
                return;
            }
            TCPNetHeaderProto header = element.Item2;
            object target = element.Item1;
            if (!header.FragmentInfo.NeedFragment)
            {
                //写入消息头
                header.RequestId = requestId;
                header.ToBytes(writeBuffer, 0);
                try
                {
                    int count = serializer.Serialize(target, writeBuffer, header.HeaderLength);
                    await netClient.Send(writeBuffer, 0, count + header.HeaderLength, token);
                    requestId++;
                }
                catch (Exception)
                {
                    throw;
                }
            }
            else
            {
                MemoryStream mstream;
                if (bigBuffers.Count > 0 && bigBuffers.TryTake(out mstream))
                {
                }
                else
                {
                    mstream = new MemoryStream();
                    mstream.Capacity = FrameBufferSize * 2;
                }
                bigFrames.TryAdd(requestId, mstream);
                serializer.Serialize(target, mstream);
                while (true)
                {
                    //写入消息头
                    header.RequestId = requestId;
                    header.ToBytes(writeBuffer, 0);
                    //
                    int writeLength = (int)Math.Min(writeBuffer.Length - header.HeaderLength, mstream.Length - mstream.Position);
                    mstream.Write(writeBuffer, header.HeaderLength, writeLength);
                    await netClient.Send(writeBuffer, 0, writeLength + header.HeaderLength, token);
                    requestId++;
                    if (mstream.Position >= mstream.Length)
                    {
                        break;
                    }
                }
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

                    if (!messageQueue.TryDequeue(out var item))
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
            }
        }
    }
}
