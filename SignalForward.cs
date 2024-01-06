using SignalForward.UDP;
using System.Net;
using System.Reflection;

namespace SignalForward
{
    public partial class SignalForward : Form
    {
        public log4net.ILog? log;

        /// <summary>
        /// 远程Udp对象
        /// </summary>
        private xxUDPSyncServer? _remoteUdp;

        /// <summary>
        /// PLC
        /// </summary>
        private IPEndPoint _plcIpEndPoint;

        /// <summary>
        /// AOI1
        /// </summary>
        private IPEndPoint _Aoi1PortEndPoint;
        /// <summary>
        /// AOI2
        /// </summary>
        private IPEndPoint _Aoi2PortEndPoint;

        /// <summary>
        /// 本地连接
        /// </summary>
        private xxUDPSyncServer? _localUdp;

        /// <summary>
        /// 本地连接1
        /// </summary>
        private xxUDPSyncServer? _localUdp1;

        /// <summary>
        /// 通讯信号
        /// </summary>
        public WHCurrentQueue<Byte[]>? RemoteQueue;

        public SignalForward()
        {
            log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
            RemoteQueue = new WHCurrentQueue<Byte[]>("", log);
            InitializeComponent();
            Task.Factory.StartNew(Communication, TaskCreationOptions.LongRunning);
        }

        public void Communication()
        {
            byte[] data;

            while (true)
            {
                if (_localUdp == null || _localUdp1 == null)
                {
                    continue;
                }
                RemoteQueue.Dequeue(out data);
                switch (data[43])
                {
                    case 1:
                        if (data[3] == 1)
                        {
                            _localUdp.Send(_Aoi1PortEndPoint, data);
                            bool control = true;
                            while (control)
                            {
                                IPEndPoint rEndPoint = default;
                                byte[] buff = _localUdp._server.Receive(ref rEndPoint);
                                if (buff != default)
                                {
                                    _remoteUdp.SendAsync(_plcIpEndPoint, buff);
                                }
                                if (buff[2] == 2)
                                {
                                    break;
                                }
                            }
                        }
                        break;
                    case 2:
                        if (data[3] == 1)
                        {
                            _localUdp1.Send(_Aoi2PortEndPoint, data);
                            bool control = true;
                            while (control)
                            {
                                IPEndPoint rEndPoint = default;
                                byte[] buff = _localUdp1._server.Receive(ref rEndPoint);
                                if (buff != default)
                                {
                                    _remoteUdp.SendAsync(_plcIpEndPoint, buff);
                                }
                                if (buff[2] == 2)
                                {
                                    break;
                                }
                            }
                        }
                        break;
                    case 3:
                        if (data[3] == 1)
                        {
                            //事务列表
                            List<Task<byte[]>> tasks = new List<Task<byte[]>>();
                            _localUdp.SendAsync(_Aoi1PortEndPoint, data);
                            _localUdp1.SendAsync(_Aoi2PortEndPoint, data);
                            bool control = true;
                            while (control)
                            {
                                Task<byte[]> result = new Task<byte[]>((() =>
                                {
                                    IPEndPoint ipEnd = default;
                                    return _localUdp._server.Receive(ref ipEnd);
                                }));
                                tasks.Add(result);
                                Task<byte[]> result1 = new Task<byte[]>((() =>
                                {
                                    IPEndPoint ipEnd = default;
                                    return _localUdp1._server.Receive(ref ipEnd);
                                }));
                                tasks.Add(result1);
                                result.Start();
                                result1.Start();

                                List<byte[]> r = new List<byte[]>();
                                foreach (var item in tasks)
                                {
                                    r.Add(item.Result);
                                }

                                if (r[0][2] == 2 && r[1][2] == 2)
                                {
                                    break;
                                }
                            }
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        private void RemoteBnt_Click_1(object sender, EventArgs e)
        {
            _plcIpEndPoint = new IPEndPoint(IPAddress.Parse(PlcIp.Text.Trim()), int.Parse(PlcPort.Text.Trim()));

            _remoteUdp = new xxUDPSyncServer(IPAddress.Parse(Plc_oneIp.Text.Trim()), int.Parse(Plc_onePort.Text.Trim()), log);
            _remoteUdp.DataReceived += (object? sender, byte[] dataBytes) =>
            {
                RemoteQueue.Enqueue(dataBytes);
            };
            _remoteUdp.Start();
        }


        private void button1_Click(object sender, EventArgs e)
        {
            _Aoi1PortEndPoint = new IPEndPoint(IPAddress.Parse(Aoi1Ip.Text.Trim()), int.Parse(Aoi1Port.Text.Trim()));
            _localUdp = new xxUDPSyncServer(IPAddress.Parse(Aoi1_oneIp.Text.Trim()), int.Parse(Aoi_onePort.Text.Trim()), log);
        }

        private void button2_Click_1(object sender, EventArgs e)
        {
            _Aoi2PortEndPoint = new IPEndPoint(IPAddress.Parse(Aoi2Ip.Text.Trim()), int.Parse(Aoi2Port.Text.Trim()));
            _localUdp1 = new xxUDPSyncServer(IPAddress.Parse(Aoi2_oneIp.Text.Trim()), int.Parse(Aoi2_onePort.Text.Trim()), log);
        }
    }
}