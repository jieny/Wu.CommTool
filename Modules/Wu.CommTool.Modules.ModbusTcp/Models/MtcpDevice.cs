﻿using System.Diagnostics;
using System.IO.Ports;
using System.Text.RegularExpressions;

namespace Wu.CommTool.Modules.ModbusTcp.Models;

/// <summary>
/// ModbusTcp设备
/// </summary>
public partial class MtcpDevice : ObservableObject,IDisposable
{
    private static readonly ILog log = LogManager.GetLogger(typeof(MtcpDevice));
    private readonly EventWaitHandle WaitNextOne = new AutoResetEvent(true);  //等待接收完成后再发送下一条
    string currentRequest = string.Empty;

    /// <summary>
    /// 设备名
    /// </summary>
    [ObservableProperty]
    string name = "未命名";

    /// <summary>
    /// 从站地址
    /// </summary>
    [ObservableProperty]
    byte slaveAddr = 1;

    [ObservableProperty]
    [property:JsonIgnore]
    ModbusTcpClient modbusTcpClient = new();

    /// <summary>
    /// 备注
    /// </summary>
    [ObservableProperty]
    string remark;

    /// <summary>
    /// 设备状态
    /// </summary>
    [ObservableProperty]
    [property: JsonIgnore]
    DeviceState deviceState;

    /// <summary>
    /// 字节序
    /// </summary>
    [ObservableProperty]
    ModbusByteOrder modbusByteOrder = ModbusByteOrder.DCBA;

    /// <summary>
    /// 测点数据列表
    /// </summary>
    [ObservableProperty]
    ObservableCollection<MtcpData> mtcpDatas = [];

    /// <summary>
    /// 页面消息
    /// </summary>
    [ObservableProperty]
    ObservableCollection<MessageData> messages = [];

    /// <summary>
    /// 所有者
    /// </summary>
    [ObservableProperty]
    MtcpDeviceManager owner;

    [ObservableProperty]
    bool isOnline;

    [ObservableProperty]
    string serverIp = "127.0.0.1";

    [ObservableProperty]
    int serverPort = 502;

    /// <summary>
    /// 建立Tcp/Ip连接
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    [property: JsonIgnore]
    public async Task Connect()
    {
        try
        {
            //建立TcpIp连接
            ModbusTcpClient?.Dispose();
            ModbusTcpClient = new ModbusTcpClient();
            ModbusTcpClient.ClientConnecting += () =>
            {
                ShowMessage($"连接中...");
            };
            ModbusTcpClient.ClientConnected += (e) =>
            {
                IsOnline = true;
                ShowMessage($"连接服务器成功... {ServerIp}:{ServerPort}");
            };
            ModbusTcpClient.ClientDisconnected += (e) =>
            {
                IsOnline = false;
                ShowMessage("断开连接...");
            };

            ModbusTcpClient.MessageSending += ModbusTcpClient_MessageSending;
            ModbusTcpClient.MessageReceived += ModbusTcpClient_MessageReceived;
            ModbusTcpClient.ErrorOccurred += (s) =>
            {
                ShowErrorMessage(s);
            };
            await ModbusTcpClient.ConnectAsync(ServerIp, ServerPort);
        }
        catch (Exception ex)
        {
            IsOnline = ModbusTcpClient.Connected;
            ShowErrorMessage($"连接失败...{ex.Message}");
        }
    }

    /// <summary>
    /// 发送消息事件
    /// </summary>
    /// <param name="obj"></param>
    private void ModbusTcpClient_MessageSending(string obj)
    {
        ShowSendMessage(new MtcpFrame(obj));
    }

    /// <summary>
    /// 接收消息事件
    /// </summary>
    /// <param name="obj"></param>
    private void ModbusTcpClient_MessageReceived(string obj)
    {
        ShowReceiveMessage(new MtcpFrame(obj));
    }

    /// <summary>
    /// 断开Tcp连接
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    [property: JsonIgnore]
    public void DisConnect()
    {
        try
        {
            ModbusTcpClient.Close();
        }
        catch (Exception ex)
        {
            ShowErrorMessage(ex.Message);
        }
    }

    //运行数据监控任务
    public void RunMonitorTask()
    {
        Task.Run(MonitorTask);
    }

    /// <summary>
    /// 数据监控任务
    /// </summary>
    private async void MonitorTask()
    {
        AnalyzeDataAddress();  //分析数据帧
        await Connect();//打开tcp连接
        WaitNextOne.Set();
        while (Owner.State)
        {
            foreach (var frame in RequestFrames)
            {
                ExecutePublishMessage(frame);
                await Task.Delay(50);//该处可以设定延时
                WaitNextOne.WaitOne(1000);//等待接收上一条指令的应答,最大等待1000ms
            }
        }
        this.Dispose();
    }

    /// <summary>
    /// 执行发送帧
    /// </summary>
    private bool ExecutePublishMessage(string message)
    {
        try
        {
            //发送数据不能为空
            if (message is null || message.Length.Equals(0))
            {
                return false;
            }

            //验证数据字符必须符合16进制
            Regex regex = new(@"^[0-9 a-f A-F -]*$");
            if (!regex.IsMatch(message))
            {
                return false;
            }

            byte[] data;
            try
            {
                data = message.Replace("-", string.Empty).GetBytes();
            }
            catch
            {
                return false;
            }

            if (ModbusTcpClient.Connected)
            {
                try
                {
                    currentRequest = message;//设置当前发送的帧,用于接收数据时确定测点地址范围
                    ModbusTcpClient.SendMessage(message);//发送数据
                    //Debug.Write($"发送:{message}");
                    return true;
                }
                catch (Exception ex)
                {
                    HcGrowlExtensions.Warning(ex.Message);
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 读取数据的请求帧
    /// </summary>
    [JsonIgnore]
    public List<string> RequestFrames
    {
        get
        {
            if (!RequestFramesUpdated)
            {
                AnalyzeDataAddress();
            }
            return requestFrames;
        }

        set => SetProperty(ref requestFrames, value);
    }
    private List<string> requestFrames;

    private bool RequestFramesUpdated = false;

    /// <summary>
    /// 对测点进行分析,得到获取所有测点数据需要发送的请求帧
    /// </summary>
    [RelayCommand]
    [property: JsonIgnore]
    public void AnalyzeDataAddress()
    {
        List<string> frames = [];
        #region 读取保持寄存器 03 功能码
        //将需要读取数据按字节的起始地址进行排序
        //根据寄存器类型生成两组
        List<MtcpData> holdingList = [.. MtcpDatas.Where(x => x.RegisterType == RegisterType.Holding).ToList().OrderBy(x => x.RegisterAddr)];
        //对排序后的列表求并集获取所有需要读取的字节地址区间
        List<Point> holdingPoints = [];//使用Point表示闭区间
        //遍历需要读取的数据
        //当前仅做字和双字的处理  字节、bit等功能后续再完善
        foreach (var item in holdingList)
        {
            // points  [X1,Y1] [X2,Y2] [X3,Y3]
            //由于列表已经是排序过的,故当item的起始地址不在区间列表的最后一个区间上或下一个地址,就需要另起一个区间了
            if (holdingPoints.Count == 0 || holdingPoints.LastOrDefault().Y + 1 < item.RegisterAddr)
            {
                holdingPoints.Add(new Point(item.RegisterAddr, item.RegisterLastWordAddr));
            }
            //该数据的起始地址在最后一个区间但长度大于Y将最后一个区间扩大
            else if (holdingPoints.LastOrDefault().Y < item.RegisterLastWordAddr)
            {
                holdingPoints[^1] = new Point(holdingPoints.LastOrDefault().X, item.RegisterLastWordAddr);
            }
        }

        //根据区间生成请求帧
        foreach (var p in holdingPoints)
        {
            if (p.Y - p.X < 99)
            {
                frames.Add(ModbusUtils.StrCombineCrcCode($"{SlaveAddr:X2}03{(int)p.X:X4}{(int)(p.Y - p.X + 1):X4}"));
            }
            //若有超过100字节的则再次拆分(设备厂商不同,有些设备支持最大读取数量不同)
            else
            {
                var startAddr = (int)p.X;
                //拆分成一帧读62字
                frames.Add(ModbusUtils.StrCombineCrcCode($"{SlaveAddr:X2}03{startAddr:X4}{62:X4}"));
                startAddr += 58;//两帧之间读取的地址重叠4字,可以保证在临界的数据至少在其中一帧是完整的
                while (true)
                {
                    if (p.Y - startAddr < 99)
                    {
                        frames.Add(ModbusUtils.StrCombineCrcCode($"{SlaveAddr:X2}03{startAddr:X4}{(int)(p.Y - startAddr + 1):X4}"));
                        break;//退出循环
                    }
                    else
                    {
                        frames.Add(ModbusUtils.StrCombineCrcCode($"{SlaveAddr:X2}03{startAddr:X4}{62:X4}"));
                    }
                    startAddr += 58;
                }
            }
        }
        #endregion


        #region 读取输入寄存器 04功能码
        //将需要读取数据按字节的起始地址进行排序
        //输入寄存器
        List<MtcpData> inputList = [.. MtcpDatas.Where(x => x.RegisterType == RegisterType.Input).ToList().OrderBy(x => x.RegisterAddr)];
        //对排序后的列表求并集获取所有需要读取的字节地址区间
        List<Point> inputPoints = [];//使用Point表示闭区间
        //遍历需要读取的数据
        //当前仅做字和双字的处理  字节、bit等功能后续再完善
        foreach (var item in inputList)
        {
            // points  [X1,Y1] [X2,Y2] [X3,Y3]
            //由于列表已经是排序过的,故当item的起始地址不在区间列表的最后一个区间上或下一个地址,就需要另起一个区间了
            if (inputPoints.Count == 0 || inputPoints.LastOrDefault().Y + 1 < item.RegisterAddr)
            {
                inputPoints.Add(new Point(item.RegisterAddr, item.RegisterLastWordAddr));
            }
            //该数据的起始地址在最后一个区间但长度大于Y将最后一个区间扩大
            else if (inputPoints.LastOrDefault().Y < item.RegisterLastWordAddr)
            {
                inputPoints[^1] = new Point(inputPoints.LastOrDefault().X, item.RegisterLastWordAddr);
            }
        }

        //根据区间生成请求帧
        foreach (var p in inputPoints)
        {
            if (p.Y - p.X < 99)
            {
                frames.Add(ModbusUtils.StrCombineCrcCode($"{SlaveAddr:X2}04{(int)p.X:X4}{(int)(p.Y - p.X + 1):X4}"));
            }
            //若有超过100字节的则再次拆分(设备厂商不同,有些设备支持最大读取数量不同)
            else
            {
                var startAddr = (int)p.X;
                //拆分成一帧读62字
                frames.Add(ModbusUtils.StrCombineCrcCode($"{SlaveAddr:X2}04{startAddr:X4}{62:X4}"));
                startAddr += 58;//两帧之间读取的地址重叠4字,可以保证在临界的数据至少在其中一帧是完整的
                while (true)
                {
                    if (p.Y - startAddr < 99)
                    {
                        frames.Add(ModbusUtils.StrCombineCrcCode($"{SlaveAddr:X2}04{startAddr:X4}{(int)(p.Y - startAddr + 1):X4}"));
                        break;//退出循环
                    }
                    else
                    {
                        frames.Add(ModbusUtils.StrCombineCrcCode($"{SlaveAddr:X2}04{startAddr:X4}{62:X4}"));
                    }
                    startAddr += 58;
                }
            }
        }
        #endregion

        //赋值帧列表
        RequestFrames = frames;
        RequestFramesUpdated = true;
    }

    [RelayCommand]
    [property: JsonIgnore]
    private void AddNewMtcpData(MtcpData mtcpData)
    {
        if (mtcpData == null || !MtcpDatas.Contains(mtcpData))
        {
            MtcpDatas.Add(new MtcpData());
            return;
        }
        else
        {
            //基于选择的测点,生成新的测点数据
            var n = new MtcpData()
            {
                RegisterType = mtcpData.RegisterType,//相同的寄存器类型
                ModbusDataType = mtcpData.ModbusDataType,//相同的数据类型
                RegisterAddr = (ushort)Math.Floor(mtcpData.RegisterLastWordAddr + 1),//根据上一个数据计算当前的地址
            };
            MtcpDatas.Insert(MtcpDatas.IndexOf(mtcpData) + 1, n);
        }
    }

    [RelayCommand]
    [property: JsonIgnore]
    private void DeleteMtcpData(MtcpData MtcpData)
    {
        try
        {
            if (MtcpDatas.Contains(MtcpData))
            {
                MtcpDatas.Remove(MtcpData);
            }
        }
        catch (Exception ex)
        {
            HcGrowlExtensions.Warning(ex.Message);
        }
    }

    public override string ToString()
    {
        return $"{Name} 从站:{SlaveAddr}";
    }

    /// <summary>
    /// 更新设备通讯状态
    /// </summary>
    public void UpdateState()
    {
        //var onlineCount = MtcpDatas.Where(x => x.State == true).Count();
        //if (onlineCount == MtcpDatas.Count)
        //{
        //    DeviceState = DeviceState.Online;//全部在线
        //}
        //else if (onlineCount == 0)
        //{
        //    DeviceState = DeviceState.Offline;//全部离线
        //}
        //else if (onlineCount < MtcpDatas.Count)
        //{
        //    DeviceState = DeviceState.Warning;//存在离线的测点
        //}
    }


    #region******************************  页面消息  ******************************
    /// <summary>
    /// 页面显示消息
    /// </summary>
    /// <param name="message"></param>
    /// <param name="type"></param>
    public void ShowMessage(string message, MessageType type = MessageType.Info)
    {
        try
        {
            void action()
            {
                Messages.Add(new MessageData($"{message}", DateTime.Now, type));
                log.Info(message);
                while (Messages.Count > 260)
                {
                    Messages.RemoveAt(0);
                }
            }
            Wu.Wpf.Utils.ExecuteFunBeginInvoke(action);
        }
        catch (Exception) { }
    }

    /// <summary>
    /// 错误消息
    /// </summary>
    /// <param name="message"></param>
    public void ShowErrorMessage(string message) => ShowMessage(message, MessageType.Error);

    /// <summary>
    /// 页面展示接收数据消息
    /// </summary>
    /// <param name="frame"></param>
    public void ShowReceiveMessage(MtcpFrame frame)
    {
        try
        {
            void action()
            {
                var msg = new MtcpMessageData("", DateTime.Now, MessageType.Receive, frame);
                Messages.Add(msg);
                log.Info($"接收:{frame}");
                while (Messages.Count > 200)
                {
                    Messages.RemoveAt(0);
                }
            }
            Wu.Wpf.Utils.ExecuteFunBeginInvoke(action);
        }
        catch (Exception) { }
    }

    /// <summary>
    /// 页面展示发送数据消息
    /// </summary>
    /// <param name="frame"></param>
    public void ShowSendMessage(MtcpFrame frame)
    {
        try
        {
            void action()
            {
                var msg = new MtcpMessageData("", DateTime.Now, MessageType.Send, frame);
                Messages.Add(msg);
                log.Info(message: $"发送:{frame}");
                while (Messages.Count > 200)
                {
                    Messages.RemoveAt(0);
                }
            }
            Wu.Wpf.Utils.ExecuteFunBeginInvoke(action);
        }
        catch (System.Exception) { }
    }


    /// <summary>
    /// 清空消息
    /// </summary>
    [RelayCommand]
    [property: JsonIgnore]
    public void MessageClear()
    {
        Messages.Clear();
    }

    /// <summary>
    /// 暂停更新接收的数据
    /// </summary>
    [RelayCommand]
    public void Pause()
    {
        //IsPause = !IsPause;
        //if (IsPause)
        //{
        //    ShowMessage("暂停更新接收的数据");
        //}
        //else
        //{
        //    ShowMessage("恢复更新接收的数据");
        //}
    }

    #endregion
    public void Dispose()
    {

    }
}