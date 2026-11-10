using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DeathKnight.Net
{
    public class Tools
    {
        public static NetworkReachability GetNetType()
        {
            //NetworkReachability.NotReachable 无可用网络
            //NetworkReachability.ReachableViaCarrierDataNetwork 运营商蜂窝网络
            //NetworkReachability.ReachableViaLocalAreaNetwork Wi-Fi 或有线网络
            return Application.internetReachability;
        }

    }
}
