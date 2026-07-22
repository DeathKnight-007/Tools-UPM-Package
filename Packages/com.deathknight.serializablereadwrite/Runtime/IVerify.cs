using System.IO;

namespace SerializableReadWrite
{
    /// <summary>
    /// 验证文件完整性，以及无修改
    /// </summary>
    public interface IVerify 
    {
        int TagLength { get; }
        /// <summary>
        /// 计算校验码，一般将校验码放到数据最后。
        /// 这个校验码实际上是一个值。整体就是无尽的内容，映射到256bit大的值区间上。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="passward"></param>
        /// <returns></returns>
        byte[] ComputeTag(byte[] data, byte[] passward = null);

        bool VerifyTag(byte[] data, byte[] tag, byte[] passward = null);

        /// <summary>
        /// 数据校验码计算，改为流式计算方式
        /// </summary>
        /// <param name="data"></param>
        /// <param name="passward"></param>
        /// <returns></returns>
        public byte[] ComputeTag(Stream data, byte[] passward = null);

        public bool VerifyTag(Stream data, byte[] tag, byte[] passward = null);
    }
}
