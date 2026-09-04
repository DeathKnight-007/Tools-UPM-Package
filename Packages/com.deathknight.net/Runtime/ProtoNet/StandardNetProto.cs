using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DeathKnight.Net
{
    public class StandardNetProto
    {
        public StandardNetProto()
        {
            HeaderLength = 16;
        }
        //16 字节消息头
        //偏移  长度    字段            说明
        //0	    4	    Magic           固定魔数，例如 0x444B4E54
        //4	    1	    Version         协议版本
        //5	    1	    Flags           请求、响应、错误等标记
        //6	    2	    MessageType     消息类型
        //8	    4	    RequestId       请求与响应匹配
        //12	4	    PayloadLength   消息体字节数
        public struct FrameHeader
        {
            public int Magic;
            public byte Version;
            public byte Flags;
            public short MessageType;
            public int RequestId;
            public int PayloadLength;
        }
        public short HeaderLength { get; }
        //public void EncodeHeader(byte[] )
    }
}
