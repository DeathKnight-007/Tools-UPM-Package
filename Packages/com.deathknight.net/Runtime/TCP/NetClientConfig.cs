using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DeathKnight.Net
{
    public class NetClientConfig
    {
        /// <summary>
        /// 服务器身份验证信息,为空则表示不需要验证服务器身份
        /// </summary>
        public CertificationInfo certificationInfo;


        private int receiveTimeout;
        /// <summary>
        /// 接收超时设置,即多长时间没有读到数据,<=0 使用默认值30s
        /// </summary>
        public int ReceiveTimeout
        {
            get
            {
                if (receiveTimeout > 0)
                    return receiveTimeout;
                else
                    return 30000;
            }
            set
            {
                receiveTimeout = value;
            }
        }

        private int writeTimeout;
        /// <summary>
        /// 发送超时设置，递交给操作系统数据的时长，影响这个时长的一般就是，发送慢，缓存满了得一直等待，<=0 使用默认值30s
        /// </summary>
        public int WriteTimeout
        {
            get
            {
                if (writeTimeout > 0)
                    return writeTimeout;
                else
                    return 30000;
            }
            set
            {
                writeTimeout = value;
            }
        }
    }
}
