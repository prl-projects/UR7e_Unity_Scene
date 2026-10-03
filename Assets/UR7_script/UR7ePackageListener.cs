using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Unity.VisualScripting;

namespace Assets.Scripts
{
    /// <summary>
    /// UR7ePackageListener
    ///
    /// 功能：
    /// 1. 透過 TCP 連線至 URSim 或真實 UR 機械手臂。
    /// 2. 持續接收 UR Robot State Package。
    /// 3. 解析 Joint Data（六軸角度）。
    /// 4. 解析 Cartesian Info（TCP Pose）。
    /// 5. 提供 SendCommand() 傳送 URScript。
    ///
    /// 使用介面：
    /// Primary Client Interface (Port 30001)
    ///
    /// 接收資料：
    /// Robot State Message (Type = 16)
    ///
    /// 解析內容：
    /// • Joint Data (SubPackage = 1)
    /// • Cartesian Info (SubPackage = 4)
    /// </summary>
    public class UR7ePackageListener
    {
        // ==================================================
        // UR Primary Interface Constants
        // ==================================================

        /// <summary>
        /// Primary Client Interface Port
        /// </summary>
        private const int Port = 30001;

        /// <summary>
        /// Package Header 長度：
        /// 4 Bytes Length + 1 Byte Type
        /// </summary>
        private const int HeaderSize = 5;

        /// <summary>
        /// Robot State Message Type
        /// </summary>
        private const int RobotStateMessageType = 16;

        /// <summary>
        /// Joint Data SubPackage
        /// </summary>
        private const int JointDataSubPackageType = 1;

        /// <summary>
        /// Cartesian Info SubPackage
        /// </summary>
        private const int CartesianInfoSubPackageType = 4;

        /// <summary>
        /// UR 固定為六軸。
        /// </summary>
        private const int JointCount = 6;

        /// <summary>
        /// 每個 Joint Data Block 長度。
        /// 詳細格式請參考 UR Primary Interface 文件。
        /// </summary>
        private const int JointDataBlockSize = 41;

        // ==================================================
        // TCP Connection
        // ==================================================

        private TcpClient client;
        private NetworkStream stream;
        private Thread listenThread;

        /// <summary>
        /// 控制背景接收執行緒是否持續運作。
        /// </summary>
        private volatile bool running;

        // ==================================================
        // Receive Buffers
        // ==================================================

        /// <summary>
        /// Package Header Buffer
        /// </summary>
        private readonly byte[] headerBuffer = new byte[HeaderSize];

        // ==================================================
        // Public Robot State
        // ==================================================

        /// <summary>
        /// 六軸目前角度（Radians）。
        /// </summary>
        public double[] JointAnglesRad { get; private set; } =
            new double[JointCount];

        /// <summary>
        /// TCP Pose
        ///
        /// [0] X
        /// [1] Y
        /// [2] Z
        /// [3] Rx
        /// [4] Ry
        /// [5] Rz
        /// </summary>
        public double[] TcpPose { get; private set; } =
            new double[JointCount];

        /// <summary>
        /// 是否已成功連線至 UR。
        /// </summary>
        public bool Connected
        {
            get
            {
                return client != null && client.Connected;
            }
        }

        // ==================================================
        // Connection
        // ==================================================

        /// <summary>
        /// 連線至 UR Robot。
        /// </summary>
        /// <param name="address">UR Robot IP</param>
        public void Connect(string address)
        {
            Close();

            IPAddress ip = IPAddress.Parse(address);

            client = new TcpClient();
            client.Connect(ip, Port);

            stream = client.GetStream();

            running = true;

            listenThread = new Thread(ListenLoop);
            listenThread.IsBackground = true;
            listenThread.Start();
        }

        /// <summary>
        /// 關閉 TCP 連線。
        /// </summary>
        public void Close()
        {
            running = false;

            try { stream?.Close(); }
            catch { }

            try { client?.Close(); }
            catch { }

            stream = null;
            client = null;
        }

        // ==================================================
        // Receive Loop
        // ==================================================

        /// <summary>
        /// 持續接收 Robot State Package。
        /// 每收到一個 Package 就解析一次。
        /// </summary>
        private void ListenLoop()
        {
            while (running &&
                   client != null &&
                   client.Connected)
            {
                try
                {
                    //--------------------------------------------------
                    // Read Header
                    //--------------------------------------------------

                    ReadExact(headerBuffer, 0, HeaderSize);

                    int packageLength = ReadInt32BE(headerBuffer, 0);
                    byte messageType = headerBuffer[4];

                    //--------------------------------------------------
                    // Read Package Body
                    //--------------------------------------------------

                    byte[] packageBuffer =
                        new byte[packageLength - HeaderSize];

                    ReadExact(
                        packageBuffer,
                        0,
                        packageBuffer.Length
                    );

                    //--------------------------------------------------
                    // Parse Robot State
                    //--------------------------------------------------

                    if (messageType == RobotStateMessageType)
                    {
                        ParseRobotState(packageBuffer);
                    }
                }
                catch
                {
                    Close();
                    break;
                }
            }
        }

        // ==================================================
        // Package Parser
        // ==================================================

        /// <summary>
        /// Robot State Package 內包含許多 SubPackage。
        /// 逐一解析需要的資料。
        /// </summary>
        /// 

        // For receiving digital input/output bits from the masterboard data subpackage
        private const int MasterboardDataSubPackageType = 3;
        public int DigitalInputBits { get; private set; }
        public int DigitalOutputBits { get; private set; }
        public bool GetDigitalOut(int i) => ((DigitalOutputBits >> i) & 1) != 0;
        private void ParseRobotState(byte[] buffer)
        {
            int pointer = 0;

            while (pointer + HeaderSize <= buffer.Length)
            {
                int subPackageLength =
                    ReadInt32BE(buffer, pointer);

                byte subPackageType =
                    buffer[pointer + 4];

                if (subPackageLength <= 0 ||
                    pointer + subPackageLength > buffer.Length)
                {
                    break;
                }

                int dataStart =
                    pointer + HeaderSize;

                switch (subPackageType)
                {
                    case JointDataSubPackageType:
                        ParseJointData(buffer, dataStart);
                        break;

                    case CartesianInfoSubPackageType:
                        ParseCartesianInfo(buffer, dataStart);
                        break;

                    // case for Masterboard Data SubPackage, now digital input/output bits can be read as 4.
                    case MasterboardDataSubPackageType:
                        DigitalInputBits = ReadInt32BE(buffer, dataStart);
                        DigitalOutputBits = ReadInt32BE(buffer, dataStart + 4);
                        break;
                }

                pointer += subPackageLength;
            }
        }

        // ==================================================
        // Joint Data
        // ==================================================

        /// <summary>
        /// 解析六軸目前角度（Radians）。
        /// </summary>
        private void ParseJointData(byte[] buffer, int start)
        {
            for (int i = 0; i < JointCount; i++)
            {
                int offset =
                    start + i * JointDataBlockSize;

                if (offset + 8 <= buffer.Length)
                {
                    JointAnglesRad[i] =
                        ReadDoubleBE(buffer, offset);
                }
            }
        }

        // ==================================================
        // Cartesian Info
        // ==================================================

        /// <summary>
        /// 解析目前 TCP Pose。
        /// </summary>
        private void ParseCartesianInfo(
            byte[] buffer,
            int start)
        {
            for (int i = 0; i < JointCount; i++)
            {
                int offset =
                    start + i * 8;

                if (offset + 8 <= buffer.Length)
                {
                    TcpPose[i] =
                        ReadDoubleBE(buffer, offset);
                }
            }
        }

        // ==================================================
        // Send URScript
        // ==================================================

        /// <summary>
        /// 傳送單行 URScript。
        ///
        /// 範例：
        /// movej(...)
        /// set_digital_out(...)
        /// popup(...)
        /// </summary>
        public void SendCommand(string command)
        {
            if (stream == null ||
                client == null ||
                !client.Connected)
            {
                return;
            }

            byte[] data =
                Encoding.ASCII.GetBytes(command + "\n");

            stream.Write(data, 0, data.Length);
        }

        // ==================================================
        // Socket Utility
        // ==================================================

        /// <summary>
        /// 確保讀滿指定 Byte 數。
        /// Socket.Read() 不保證一次就讀完。
        /// </summary>
        private void ReadExact(
            byte[] buffer,
            int offset,
            int size)
        {
            int totalRead = 0;

            while (totalRead < size)
            {
                int read =
                    stream.Read(
                        buffer,
                        offset + totalRead,
                        size - totalRead);

                if (read <= 0)
                    throw new Exception("Socket closed");

                totalRead += read;
            }
        }

        // ==================================================
        // Binary Parser
        // ==================================================

        /// <summary>
        /// 讀取 Big Endian Int32。
        /// </summary>
        private static int ReadInt32BE(
            byte[] buffer,
            int offset)
        {
            return
                (buffer[offset] << 24) |
                (buffer[offset + 1] << 16) |
                (buffer[offset + 2] << 8) |
                buffer[offset + 3];
        }

        /// <summary>
        /// 讀取 Big Endian Double。
        /// </summary>
        private static double ReadDoubleBE(
            byte[] buffer,
            int offset)
        {
            byte[] temp = new byte[8];

            for (int i = 0; i < 8; i++)
            {
                temp[i] = buffer[offset + 7 - i];
            }

            return BitConverter.ToDouble(temp, 0);
        }
    }
}