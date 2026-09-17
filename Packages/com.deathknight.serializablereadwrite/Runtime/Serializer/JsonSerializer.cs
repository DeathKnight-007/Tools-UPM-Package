using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using System.Text;
using System;

namespace SerializableReadWrite
{
    public class JsonSerializer : ISerializer
    {
        private UTF8Encoding utf8 = new UTF8Encoding(false);
        public T Deserialize<T>(byte[] buffer, int offset, int count)
        {
            string content = utf8.GetString(buffer, offset, count);
            return JsonConvert.DeserializeObject<T>(content);
        }

        public T Deserialize<T>(Stream stream)
        {
            using (StreamReader sr = new StreamReader(stream, utf8, false, 1024 * 16, true))
            {
                using (JsonTextReader jr = new JsonTextReader(sr))
                {
                    jr.CloseInput = false;
                    return Newtonsoft.Json.JsonSerializer.CreateDefault().Deserialize<T>(jr);
                }
            }
        }

        public T Deserialize<T>(string content)
        {
            return JsonConvert.DeserializeObject<T>(content);
        }

        public int Serialize<T>(T target, byte[] buffer, int offset)
        {
            string content = JsonConvert.SerializeObject(target);
            return utf8.GetBytes(content, 0, content.Length, buffer, offset);
        }

        public byte[] Serialize<T>(T target)
        {
            string content = JsonConvert.SerializeObject(target);
            return utf8.GetBytes(content);
        }

        public void Serialize<T>(T target, Stream stream)
        {
            using (StreamWriter sw = new StreamWriter(stream, utf8, 1024 * 16, true))
            {
                using (JsonTextWriter jw = new JsonTextWriter(sw))
                {
                    jw.CloseOutput = false;
                    Newtonsoft.Json.JsonSerializer.CreateDefault().Serialize(jw, target);
                }
            }
        }

        public string SerializeToString<T>(T target)
        {
             return JsonConvert.SerializeObject(target);
        }
    }
}
