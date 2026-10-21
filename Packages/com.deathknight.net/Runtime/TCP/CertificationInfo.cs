using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using UnityEngine;

namespace DeathKnight.Net
{
    public class CertificationInfo
    {
        /// <summary>
        /// 服务器证书中的某个可选名字
        /// </summary>
        public string ServerAlternativeName;

        /// <summary>
        /// 服务器证书中的可选名字列表
        /// </summary>
        public string[] ServerAlternativeNames;

        /// <summary>
        /// 服务器下发的证书链，用于验证服务器身份，包含中间证书机构（也是一个证书，但是这个证书具有颁发权限）和中间机构颁发的证书
        /// </summary>
        public X509Certificate2Collection ServerCertificates;

        public void CheckCertification()
        {

        }
    }
}
