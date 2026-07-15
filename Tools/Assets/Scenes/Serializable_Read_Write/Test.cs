using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SerializableReadWrite
{
    public class Test : MonoBehaviour
    {
        public InputField filePath;
        public InputField writePath;
        public Button savebtn;
        public Button readbtn;
        // Start is called before the first frame update
        void Start()
        {
            filePath.text = "D:/Test.txt";
            writePath.text = "D:/temp.txt";
            TestData data = new TestData();
            data.id = 14;
            data.age = 32;
            data.name = "zsp";

            string passward = "zsp0617";
            string verifyPassward = "zsp7789";
            IVerify verify = new HMACVerify();

            readbtn.onClick.AddListener(
            () =>
            {
                ObjectSaveRead.Read<TestData>(writePath.text, passward, verify, verifyPassward).PrintInfo();
            });

            savebtn.onClick.AddListener(
                () =>
                {
                    data.content = File.ReadAllBytes(filePath.text);
                    ObjectSaveRead.Save<TestData>(writePath.text, data, passward, verify, verifyPassward);
                });
        }

        // Update is called once per frame
        void Update()
        {
        
        }
    }
}
