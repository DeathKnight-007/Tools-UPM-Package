using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace DeathKnight.Net
{
    public interface IIPAddressSelectStrategy<T> where T : NetSimpleClient, new()
    {
        Task<T> Select(IPEndPoint[] ips, NetClient.NetClientConfig config);
    }
}
