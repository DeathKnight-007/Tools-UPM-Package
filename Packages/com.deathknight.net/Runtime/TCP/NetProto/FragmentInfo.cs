using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DeathKnight.Net
{
    public struct FragmentInfo
    {
        /// <summary>
        /// 是否需要分片
        /// </summary>
        public bool NeedFragment;

        /// <summary>
        /// 这个大片的id，以大片的第一片的RequestId命名
        /// </summary>
        public long FragmentId;

        /// <summary>
        /// 片序列号，从1开始
        /// </summary>
        public short Index;

        /// <summary>
        /// 片总数是多少
        /// </summary>
        public short TotalCount;
    }
}
