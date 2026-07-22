using System.IO;
using System.Security.Cryptography;

namespace SerializableReadWrite
{
    public class HMACVerify : IVerify
    {
        public int TagLength
        {
            get
            {
                return 32;
            }
        }

        /// <summary>
        /// 哈希计算结果相关因素， 1、文件内容 2、密码。 修改者不知道密码，就不能修改文件后重新计算哈希值，从而绕过检查
        /// </summary>
        /// <param name="data"></param>
        /// <param name="passward"></param>
        /// <returns></returns>
        public byte[] ComputeTag(byte[] data, byte[] passward)
        {
            using ( HMACSHA256 hmac = new HMACSHA256(passward))
            {
                return hmac.ComputeHash(data);
            }
        }

        public bool VerifyTag(byte[] data, byte[] tag, byte[] passward = null)
        {
            using (HMACSHA256 hmac = new HMACSHA256(passward))
            {
                byte[] dataTag = hmac.ComputeHash(data);
                return CryptographicOperations.FixedTimeEquals(dataTag, tag); // 普通对比是按顺序对比，这个是按随机顺序对比，防止破解者通过耗时猜测出密码
            }
        }

        public byte[] ComputeTag(Stream data, byte[] passward = null)
        {
            using (HMACSHA256 hmac = new HMACSHA256(passward))
            {
                return hmac.ComputeHash(data);
            }
        }

        public bool VerifyTag(Stream data, byte[] tag, byte[] passward = null)
        {
            using (HMACSHA256 hmac = new HMACSHA256(passward))
            {
                byte[] dataTag = hmac.ComputeHash(data);
                return CryptographicOperations.FixedTimeEquals(dataTag, tag);
            }
        }
    }
}
