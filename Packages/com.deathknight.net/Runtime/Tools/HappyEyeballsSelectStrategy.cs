using DeathKnight.Net;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

public sealed class HappyEyeballsSelectStrategy<T> : IIPAddressSelectStrategy<T> where T : NetSimpleClient, new()
{
    /// <summary>
    /// 每个候选地址之间的启动间隔。
    /// </summary>
    private readonly int _connectionAttemptDelayMs;

    public HappyEyeballsSelectStrategy(
        int connectionAttemptDelayMs = 250)
    {
        _connectionAttemptDelayMs = connectionAttemptDelayMs;
    }

    private NetClient.NetClientConfig config;
    public async Task<T> Select(IPEndPoint[] addresses, NetClient.NetClientConfig config)
    {
        if (addresses == null)
            throw new ArgumentNullException(nameof(addresses));

        if (addresses.Length == 0)
            throw new ArgumentException(
                "IPEndPoint 数组不能为空",
                nameof(addresses));

        this.config = config;

        List<IPEndPoint> candidates = BuildCandidateList(addresses);

        var tasks = new List<Task<ConnectResult>>();

        for (int i = 0; i < candidates.Count; i++)
        {
            IPEndPoint endPoint = candidates[i];

            // 开启下一个ip的尝试
            tasks.Add(TryConnectAsync(endPoint));

            // 最后一个不需要继续等待 250ms，
            // 等待_connectionAttemptDelayMs时长，或者之前启动的任务有完成的。就往下执行
            if (i < candidates.Count - 1)
            {
                Task delayTask = Task.Delay(_connectionAttemptDelayMs);

                IEnumerable<Task> taskSequence = tasks; // 协变转换
                var result = taskSequence.Append(delayTask);

                _ = await Task.WhenAny(result);
            }

            var success = GetSuccessfulResult(tasks);

            // 有完成的，就返回
            if (success != null)
            {
                CloseAll(tasks, success);
                return success.Client;
            }

            tasks.RemoveAll((task) => { return task.IsFaulted || task.IsCanceled; });
        }

        // 所有候选连接都已经启动。
        // 谁先成功就返回谁。
        while (tasks.Count > 0)
        {
            Task<ConnectResult> completedTask = await Task.WhenAny(tasks);

            tasks.Remove(completedTask);

            if (completedTask.Status == TaskStatus.RanToCompletion)
            {
                ConnectResult result = completedTask.Result;

                CloseAll(tasks, result);

                return result.Client;
            }
        }

        throw new Exception("happy eyeball connected failed");
    }

    /// <summary>
    /// 异步线程里建立的client们
    /// </summary>
    private ConcurrentDictionary<T, byte> poolClients = new();
    /// <summary>
    /// 实际测试一个 EndPoint 是否可以建立 TCP 连接。
    /// </summary>
    private async Task<ConnectResult> TryConnectAsync(IPEndPoint endPoint)
    {
        T client = null;
        try
        {
            client = new();
            client.Init(config);
            _ = poolClients.TryAdd(client, 0);
            await client.Connect(endPoint);
            return new ConnectResult
            {
                EndPoint = endPoint,
                Client = client
            };
        }
        catch
        {
            if (client != null)
            {
                _ = poolClients.TryRemove(client, out _);
                client.Dispose();
            }
            throw;
        }
    }

    /// <summary>
    /// IPv6 / IPv4 交错排列。
    /// 例如：
    ///
    /// IPv6-A
    /// IPv6-B
    /// IPv4-A
    /// IPv4-B
    ///
    /// →
    ///
    /// IPv6-A
    /// IPv4-A
    /// IPv6-B
    /// IPv4-B
    /// </summary>
    private List<IPEndPoint> BuildCandidateList(
        IPEndPoint[] addresses)
    {
        var ipv6 =
            new Queue<IPEndPoint>(
                addresses.Where(x =>
                    x.AddressFamily ==
                    AddressFamily.InterNetworkV6));

        var ipv4 =
            new Queue<IPEndPoint>(
                addresses.Where(x =>
                    x.AddressFamily ==
                    AddressFamily.InterNetwork));

        var result =
            new List<IPEndPoint>(addresses.Length);

        while (ipv6.Count > 0 ||
               ipv4.Count > 0)
        {
            if (ipv6.Count > 0)
                result.Add(ipv6.Dequeue());

            if (ipv4.Count > 0)
                result.Add(ipv4.Dequeue());
        }

        return result;
    }

    /// <summary>
    /// 检查已经启动的 Task 中有没有成功的。
    /// </summary>
    private ConnectResult GetSuccessfulResult(
        List<Task<ConnectResult>> tasks)
    {
        foreach (Task<ConnectResult> task in tasks)
        {
            if (task.Status == TaskStatus.RanToCompletion)
            {
                var t = task.Result;
                tasks.Remove(task);
                return t;
            }
        }

        return null;
    }

    /// <summary>
    /// 关闭测试连接。
    /// </summary>
    private void CloseAll(IEnumerable<Task<ConnectResult>> tasks, ConnectResult returnResult)
    {
        foreach(var item in poolClients)
        {
            if (item.Key != returnResult.Client)
            {
                item.Key.Dispose();
            }
        }
        poolClients.Clear();
    }

    private sealed class ConnectResult
    {
        public IPEndPoint EndPoint;
        public T Client;
    }
}