using Palmmedia.ReportGenerator.Core.Common;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Unity.Plastic.Newtonsoft.Json;
using JsonSerializer = Unity.Plastic.Newtonsoft.Json.JsonSerializer;

namespace SerializableReadWrite
{
    public static class MyObjectSaveRead
    {
        public static void Save<T>(string path, T data)
        {

            // 1、写入内存占用以及效率
            // 2、异步写入
            // 3、流式加密

            //使用AES加密
            using Aes aes = Aes.Create();//

            aes.GenerateIV(); // aes.IV,可以自动生成，也可以自己生成，但是没必要自己生成
                              // 密文初始化向量，保存在加密后的文件开头，保证数据相同，密码相同但是加密结果不同
            byte[] iv = aes.IV;

            Debug.Log("iv length:" + iv.Length);

            byte[] salt = new byte[16]; // 一般是16或者32， salt的目的是 1、随机使每个文件真正的密码不同 2、通过salt+passward计算真正的key，计算量增大
                                        // ArrayPool<byte>.Shared.Rent() 需要池化条件，1、64kb以上(85kb是托管判断大文件阈值)  2、高频创建  所以这个salt直接创建吧，反正也好回收
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            aes.Key = DeriveKey("zsp0617", salt); // 密码, 长度固定32， 让用户短密码也能生成32字节密码
                                                  //aes.GenerateKey();  密码也可以自动生成

            //使用HMAC校验
            byte[] mac;

            using (var hmac = new HMACSHA256(macKey))
            {
                mac = hmac.ComputeHash(data);
            }

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 64))
            {
                fs.Write(salt, 0, salt.Length);
                fs.Write(iv, 0, iv.Length);
                //fs.SetLength(100);  提前设置文件大小，好让操作系统和硬盘分配空间，致使文件不碎片化严重

                //fs.Position = 0; fs.Seek(0, SeekOrigin.Begin); 效果一样都是移动文件指针，read和write时，指针自动移动

                //FileMode.OpenOrCreate 设置读写模式
                // FileMode.OpenOrCreate、FileMode.Open 都是打开文件，然后文件指针设置在文件开头，但是保留了原本文件内容
                // FileMode.Create 打开文件，清空原有文本内容，并且文件指针在开头
                // FileMode.CreateNew 一般用不到，如果文件存在，就会报错，它必须是创建新文件

                //FileAccess.Write, FileShare.None 设置获取资源权限, 后者表示分享出去的权限，前者表示自己获得的权限

                //buffSize 1024 * 64, 写缓存，填满缓存，或者fs.Flush、fs.close时，调用io写入，但是io也会缓存后再真正执行写盘操作。如果一次调用的写入数据直接超过缓存，则直接不缓存了，直接写入
                using (CryptoStream cryptoStream = new CryptoStream(fs, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    using (StreamWriter sw = new StreamWriter(cryptoStream, Encoding.UTF8, 1024 * 16))
                    {
                        using (JsonTextWriter jw = new JsonTextWriter(sw))
                        {
                            JsonSerializer serializer = new JsonSerializer();
                            serializer.Serialize(jw, data);
                        }
                    }
                }
            }
        }

        public static byte[] DeriveKey(string password, byte[] salt)
        {
            using var derive = new Rfc2898DeriveBytes(
                password,
                salt,
                100000
            );

            return derive.GetBytes(32); // 32字节，AES-256
        }
    }
}
