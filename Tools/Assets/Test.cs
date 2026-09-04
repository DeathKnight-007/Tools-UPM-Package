using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyFileArchive;
using UnityEngine;

public sealed class Test : MonoBehaviour
{
    public struct Teacher
    {
        public string name;
        public int age;
        public string title;
        public void Display()
        {
            Debug.Log(name);
            Debug.Log(age);
            Debug.Log(title);
        }
    }
    private async Task Start()
    {
        Teacher teacher = new Teacher();
        teacher.name = "zsp";
        teacher.age = 103;
        teacher.title = "profencer";
        Debug.Log("start time:" + Time.time);
        await Task.Run(() => { Check(teacher); });
        Debug.Log("end time:" + Time.time);
    }
    private void Check(Teacher teacher)
    {
        byte[] buffer = new byte[1024 * 10];
        int count = SerializableReadWrite.ObjectSerilizeToByte.Serialize<Teacher>(teacher, buffer, 50);
        Teacher ut = SerializableReadWrite.ObjectSerilizeToByte.Deserialize<Teacher>(buffer, 50, count);
        ut.Display();
    }
}
