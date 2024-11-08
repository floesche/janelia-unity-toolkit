using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Janelia
{
    public class PoseReceiver : MonoBehaviour
    {
        // TODO: Should this class have its own spec? Or should fly-bowl setup push values here from its spec?
        public string server = "127.0.0.1";
        public int port = 2000;
        public int readBufferSizeBytes = 256;
        public int readBufferCount = 2;

        public GameObject[] controlled = new GameObject[2];
        public float scale = 1;
        public Vector3 angleOffsetDegs = Vector3.zero;

        public bool log = true;

        public delegate void TweakReceivedPoseDelegate(Transform transform);
        public TweakReceivedPoseDelegate tweakReceivedPoseDelegate;

        public void Start()
        {
            
            _socketMessageReader = new SocketMessageReader(HEADER, server, port, readBufferSizeBytes, readBufferCount);
            _socketMessageReader.Start();

            _received = new bool[controlled.Length];
            _position = new Vector3[controlled.Length];
            _eulerAngles = new Vector3[controlled.Length];
            for (int i = 0; i < controlled.Length; ++i) {
                _eulerAngles[i] = Vector3.zero;
             }
        }

        public void Update()
        {
            for (int i = 0; i < _received.Length; ++i)
            {
                _received[i] = false;
            }

            GetNextMessages(_received, _position, _eulerAngles);

            for (int i = 0; i < _received.Length; ++i)
            {
                GameObject obj = controlled[i];
                if (_received[i] && (obj != null))
                {
                    _position[i] *= scale;
                    _eulerAngles[i] *= Mathf.Rad2Deg;
                    _eulerAngles[i] += angleOffsetDegs;

                    obj.transform.position = _position[i];
                    obj.transform.eulerAngles = _eulerAngles[i];

                    tweakReceivedPoseDelegate?.Invoke(obj.transform);                        
                   
                }
            }
        }

        public void OnDisable()
        {
            _socketMessageReader.OnDisable();
        }

        private void GetNextMessages(bool[] received, Vector3[] position, Vector3[] eulerAngles)
        {
            long sentTimestampMs = 0;
            int id = -999;

            Byte[] dataFromSocket = null;
            long dataTimestampMs = 0;
            int i0 = -1;
            while (_socketMessageReader.GetNextMessage(ref dataFromSocket, ref dataTimestampMs, ref i0))
            {
                bool valid = true;

                long receivedTimestampMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                int i1 = 0, len1 = 0;
                IoUtilities.NthSplit(dataFromSocket, SEPARATOR, i0, 1, ref i1, ref len1);
                sentTimestampMs = IoUtilities.ParseLong(dataFromSocket, i1, len1, ref valid);
                if (!valid)
                {
                    Debug.Log("PoseReceiver.GetNextMessage() failed to parse column 1 (timestamp)");
                    return;
                }

                int i2 = 0, len2 = 0;
                IoUtilities.NthSplit(dataFromSocket, SEPARATOR, i0, 2, ref i2, ref len2);
                id = (int) IoUtilities.ParseLong(dataFromSocket, i2, len2, ref valid);
                if (!valid)
                {
                    Debug.Log("PoseReceiver.GetNextMessage() failed to parse column 2 (ID)");
                    return;
                }

                if ((0 <= id) && (id < received.Length))
                {
                    received[id] = true;

                    int i3 = 0, len3 = 0;
                    IoUtilities.NthSplit(dataFromSocket, SEPARATOR, i0, 3, ref i3, ref len3);
                    float x = (float)IoUtilities.ParseDouble(dataFromSocket, i3, len3, ref valid);
                    if (!valid)
                    {
                        Debug.Log("PoseReceiver.GetNextMessage() failed to parse column 3 (position X)");
                        return;
                    }
                    
                    int i4 = 0, len4 = 0;
                    IoUtilities.NthSplit(dataFromSocket, SEPARATOR, i0, 4, ref i4, ref len4);
                    float z = (float)IoUtilities.ParseDouble(dataFromSocket, i4, len4, ref valid);
                    if (!valid)
                    {
                        Debug.Log("PoseReceiver.GetNextMessage() failed to parse column 4 (position Y/Z)");
                        return;
                    }
                    
                    int i5 = 0, len5 = 0;
                    IoUtilities.NthSplit(dataFromSocket, SEPARATOR, i0, 5, ref i5, ref len5);
                    float y = (float)IoUtilities.ParseDouble(dataFromSocket, i5, len5, ref valid);
                    if (!valid)
                    {
                        Debug.Log("PoseReceiver.GetNextMessage() failed to parse column 5 (position Z/Y)");
                        return;
                    }

                    position[id] = new Vector3(x, y, z);

                    int i6 = 0, len6 = 0;
                    IoUtilities.NthSplit(dataFromSocket, SEPARATOR, i0, 6, ref i6, ref len6);
                    float ex = (float)IoUtilities.ParseDouble(dataFromSocket, i6, len6, ref valid);
                    if (!valid)
                    {
                        Debug.Log("PoseReceiver.GetNextMessage() failed to parse column 6 (angle X)");
                        return;
                    }

                    int i7 = 0, len7 = 0;
                    IoUtilities.NthSplit(dataFromSocket, SEPARATOR, i0, 7, ref i7, ref len7);
                    float ez = (float)IoUtilities.ParseDouble(dataFromSocket, i7, len7, ref valid);
                    if (!valid)
                    {
                        Debug.Log("PoseReceiver.GetNextMessage() failed to parse column 7 (angle Y/Z)");
                        return;
                    }

                    int i8 = 0, len8 = 0;
                    IoUtilities.NthSplit(dataFromSocket, SEPARATOR, i0, 8, ref i8, ref len8);
                    float ey = (float)IoUtilities.ParseDouble(dataFromSocket, i8, len8, ref valid);
                    if (!valid)
                    {
                        Debug.Log("PoseReceiver.GetNextMessage() failed to parse column 8 (angle Z/Y)");
                        return;
                    }

                    eulerAngles[id] = new Vector3(ex, ey, ez);

                    long finalTimestampMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                    _currentMessageLog.poseReceiverId = id;
                    _currentMessageLog.sentTimestampMs = sentTimestampMs;
                    _currentMessageLog.processedTimestampMs = finalTimestampMs;
                    _currentMessageLog.position = position[id];
                    _currentMessageLog.eulerAngles = eulerAngles[id];
                    Logger.Log(_currentMessageLog);

                    long latencyMs = finalTimestampMs - sentTimestampMs;



                    _latencySum += latencyMs;
                    _latencyCount += 1;
                    _latencyCsv.Add(
                        sentTimestampMs.ToString() + "," + 
                        receivedTimestampMs.ToString() + "," +
                        finalTimestampMs.ToString() + "," +
                        latencyMs.ToString());
                    if (_latencyCount % 1000 == 0)
                    {
                        Debug.Log("Mean latency camera -> projector" + (((float)_latencySum) / _latencyCount) + " ms");
                        WriteLatencyLog();
                        _latencySum = 0;
                        _latencyCount = 0;
                    }
                }
            }
        }

        private void WriteLatencyLog()
        {
            var dirName = "Assets/CSV_OUTPUT/";
            var dir = new DirectoryInfo(dirName);
            var fileName = "latency.log";
            var path = dirName + fileName + ".csv";
            if (!dir.Exists)
            {
                dir.Create();
            }

            if (!File.Exists(path))
            {
                // Create a file to write to.
                using (StreamWriter sw = File.CreateText(path))
                {
                    sw.WriteLine("sentTime,receivedTime,finalTime,latencySentFinal");
                }
            }

            using (StreamWriter sw = File.AppendText(path))
            {
                foreach (var str in _latencyCsv)
                {
                    sw.WriteLine(str);
                }
                _latencyCsv.Clear();
            }
        }

        private SocketMessageReader.Delimiter HEADER = SocketMessageReader.Header((Byte)'B');
        private const Byte SEPARATOR = (Byte)',';
        private SocketMessageReader _socketMessageReader;

        private bool[] _received;
        private Vector3[] _position;
        private Vector3[] _eulerAngles;

        [Serializable]
        private class PoseReceiverMessageLog : Logger.Entry
        {
            public int poseReceiverId;
            public long sentTimestampMs;
            public long processedTimestampMs;
            public Vector3 position;
            public Vector3 eulerAngles;
        };
        private PoseReceiverMessageLog _currentMessageLog = new PoseReceiverMessageLog();

        private long _latencySum = 0;
        private long _latencyCount = 0;

        private List<string> _latencyCsv = new List<string>();
    }
}