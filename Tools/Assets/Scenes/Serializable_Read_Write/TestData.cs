using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SerializableReadWrite
{
    public class TestData
    {
        public int id;
        public string name;
        public int age;
        public byte[] content;

        public void PrintInfo()
        {
            Debug.Log(id);
            Debug.Log(name);
            Debug.Log(age);
            Debug.Log(Encoding.UTF8.GetString(content));
        }
    }
}
