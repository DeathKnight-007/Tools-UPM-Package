using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DeathKnight.Net
{
    public class NetClientConfig
    {
        /// <summary>
        /// 加密策略
        /// </summary>
        public EncryptStrategy encryptStrategy;

        /// <summary>
        /// 服务器身份验证信息,为空则表示不需要验证服务器身份
        /// </summary>
        public CertificationInfo certificationInfo;

        /// <summary>
        /// 接收超时设置,即多长时间没有读到数据,<=0 表示不设置超时，这一层一般设置为0
        /// </summary>
        public int ReceiveTimeout;

        /// <summary>
        /// 发送超时设置，递交给操作系统数据的时长，影响这个时长的一般就是，发送慢，缓存满了得一直等待，<=0 表示不设置超时
        /// </summary>
        public int WriteTimeout;
    }
}
