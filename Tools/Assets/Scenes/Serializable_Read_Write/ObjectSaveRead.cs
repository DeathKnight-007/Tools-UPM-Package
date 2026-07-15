using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Unity.Plastic.Newtonsoft.Json;
using JsonSerializer = Unity.Plastic.Newtonsoft.Json.JsonSerializer;
using System;

namespace SerializableReadWrite
{
    public static class ObjectSaveRead
    {
        public static void Save<T>(string path, T data, string passward = null, IVerify verify = null, string verifyPassward = null)
        {
            // 1、写入内存占用以及效率
            // 2、异步写入
            // 3、流式加密

            // 加密
            Aes aes = null;
            byte[] salt = null;
            if (passward != null)
            {
                 aes = GetAes(passward, out salt);
            }

            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1024 * 64, false))
                {
                    // fs.SetLength(100);  提前设置文件大小，好让操作系统和硬盘分配空间，致使文件不碎片化严重

                    // fs.Position = 0; fs.Seek(0, SeekOrigin.Begin); 效果一样都是移动文件指针，read和write时，指针自动移动

                    // FileMode.OpenOrCreate 设置读写模式
                    // FileMode.OpenOrCreate、FileMode.Open 都是打开文件，然后文件指针设置在文件开头，但是保留了原本文件内容
                    // FileMode.Create 打开文件，清空原有文本内容，并且文件指针在开头
                    // FileMode.CreateNew 一般用不到，如果文件存在，就会报错，它必须是创建新文件

                    //FileAccess.Write, FileShare.None 设置获取资源权限, 后者表示分享出去的权限，前者表示自己获得的权限

                    //buffSize 1024 * 64, 写缓存，填满缓存，或者fs.Flush、fs.close时，调用io写入，但是io也会缓存后再真正执行写盘操作。如果一次调用的写入数据直接超过缓存，则直接不缓存了，直接写入

                    int dataOffset = 0; // 密文开始的位置
                    if (verify != null) // 校验数据写在文件最开头
                    {
                        fs.Position = verify.TagLength; //先把校验数据位置空出来
                        dataOffset += verify.TagLength;
                    }
                    if (aes != null)
                    {
                        fs.Write(salt, 0, salt.Length);
                        fs.Write(aes.IV, 0, aes.IV.Length);
                        dataOffset += salt.Length;
                        dataOffset += aes.IV.Length;
                        using (CryptoStream cryptoStream = new CryptoStream(fs, aes.CreateEncryptor(), CryptoStreamMode.Write, true))
                        {
                            using (StreamWriter sw = new StreamWriter(cryptoStream, Encoding.UTF8, 1024 * 16, true))
                            {
                                using (JsonTextWriter jw = new JsonTextWriter(sw))
                                {
                                    JsonSerializer serializer = new JsonSerializer();
                                    serializer.Serialize(jw, data);
                                }
                            }
                        }
                    }
                    else
                    {
                        using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8, 1024 * 16, true))
                        {
                            using (JsonTextWriter jw = new JsonTextWriter(sw))
                            {
                                JsonSerializer serializer = new JsonSerializer();
                                serializer.Serialize(jw, data);
                            }
                        }
                    }
                    //添加校验tag
                    if (verify != null)
                    {
                        fs.Seek(dataOffset, SeekOrigin.Begin); // 只对密文校验
                        byte[] tag = verify.ComputeTag(fs, verifyPassward == null ? null : Encoding.UTF8.GetBytes(verifyPassward));
                        fs.Position = 0;
                        fs.Write(tag, 0, tag.Length);
                    }
                }
            }
            finally
            {
                if (aes != null)
                {
                    aes.Dispose();
                }
            }
        }


        private static Aes GetAes(string passward, out byte[] salt)
        {
            //使用AES加密
            Aes aes = Aes.Create();//

            aes.GenerateIV(); // aes.IV,可以自动生成，也可以自己生成，但是没必要自己生成
                              // 密文初始化向量，保存在加密后的文件开头，保证数据相同，密码相同但是加密结果不同
            byte[] iv = aes.IV; // 长度是16

            salt = new byte[16]; // 一般是16或者32， salt的目的是 1、随机使每个文件真正的密码不同 2、通过salt+passward计算真正的key，计算量增大
                                        // ArrayPool<byte>.Shared.Rent() 需要池化条件，1、64kb以上(85kb是托管判断大文件阈值)  2、高频创建  所以这个salt直接创建吧，反正也好回收
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            aes.Key = DeriveKey(passward, salt); // 密码, 长度固定32， 让用户短密码也能生成32字节密码
                                                  //aes.GenerateKey();  密码也可以自动生成
            return aes;
        }

        private static byte[] DeriveKey(string password, byte[] salt)
        {
            using var derive = new Rfc2898DeriveBytes(
                password,
                salt,
                100000
            );
            return derive.GetBytes(32); // 32字节，AES-256
        }

        public static T Read<T>(string path, string passward = null, IVerify verify = null, string verifyPassward = null)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 64))
            {
                //校验tag
                if (verify != null)
                {
                    int dataOffset = 0;
                    dataOffset += verify.TagLength;
                    if(passward != null)
                    {
                        dataOffset += 32; // aes加密的salt+iv
                    }
                    byte[] tag = new byte[verify.TagLength];
                    ReadExactly(fs, tag, 0, verify.TagLength);// 读取出校验码
                    fs.Seek(dataOffset, SeekOrigin.Begin);
                    if(!verify.VerifyTag(fs, tag, verifyPassward == null ? null : Encoding.UTF8.GetBytes(verifyPassward)))
                    {
                        Exception e = new Exception("文件完整及清白校验不通过");
                        throw e;
                    }
                    fs.Seek(verify.TagLength, SeekOrigin.Begin); // 指针移动到校验码末尾
                }

                if (passward != null)
                {
                    using Aes aes = Aes.Create();
                    byte[] salt = new byte[16];
                    ReadExactly(fs, salt, 0, 16);
                    byte[] iv = new byte[16];
                    ReadExactly(fs, iv, 0, 16);
                    aes.IV = iv;
                    aes.Key = DeriveKey(passward, salt);
                    

                    using (CryptoStream cryptoStream = new CryptoStream(fs, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        using (StreamReader sr = new StreamReader(cryptoStream, Encoding.UTF8, false, 1024 * 16))
                        {
                            using (JsonTextReader jr = new JsonTextReader(sr))
                            {
                                JsonSerializer serializer = new JsonSerializer();
                                return serializer.Deserialize<T>(jr);
                            }
                        }
                    }
                }
                else
                {
                    using (StreamReader sr = new StreamReader(fs, Encoding.UTF8, false,1024 * 16))
                    {
                        using (JsonTextReader jr = new JsonTextReader(sr))
                        {
                            JsonSerializer serializer = new JsonSerializer();
                            return serializer.Deserialize<T>(jr);
                        }
                    }
                }
            }
        }
        private static void ReadExactly(
            Stream stream,
            byte[] buffer,
            int offset,
            int count)
        {
            while (count > 0)
            {
                int readCount = stream.Read(
                    buffer,
                    offset,
                    count);

                if (readCount == 0)
                    throw new EndOfStreamException("文件提前结束");

                offset += readCount;
                count -= readCount;
            }
        }
    }
}
