using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DeathKnight.Net
{
    public enum EncryptStrategy
    {
        /// <summary>
        /// 不使用加密
        /// </summary>
        None = 0,
        /// <summary>
        /// 标准TLS协议流程,官方机构颁发证书
        /// </summary>
        TLS = 1
    }
}
