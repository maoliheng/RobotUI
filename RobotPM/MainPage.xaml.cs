﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.Networking.Connectivity;

// “空白页”项模板在 http://go.microsoft.com/fwlink/?LinkId=234238 上有介绍

namespace RobotPM
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private StreamSocket clientSocket;
        private HostName serverHost;
        private bool connected = false;
        private bool closing = false;

        public MainPage()
        {
            this.InitializeComponent();
            clientSocket = new StreamSocket();
        }

        /// <summary>
        /// 建立连接
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (connected)
            {
                StatusText.Text = "Already connected";
                return;
            }

            try
            {
                StatusText.Text = "Trying to connect ...";

                serverHost = new HostName(ServerHostname.Text);
                // 发起连接
                await clientSocket.ConnectAsync(serverHost, ControlPort.Text);
                connected = true;
                StatusText.Text = "Connection established";
                //开始接受数据
                if (clientSocket.Information != null)
                {
                    await BeginReceived();
                }

            }
            catch (Exception exception)
            {
                // If this is an unknown status, 
                // it means that the error is fatal and retry will likely fail.
                if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
                {
                    throw;
                }

                StatusText.Text = "Connect failed with error: " + exception.Message;
                // Could retry the connection, but for this simple example
                // just close the socket.

                closing = true;
                // the Close method is mapped to the C# Dispose
                clientSocket.Dispose();
                clientSocket = null;
            }
        }

        /// <summary>
        /// 断开网络连接
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Disconnect_Click(object sender, RoutedEventArgs e)
        {
            closing = true;
            clientSocket.Dispose();
            clientSocket = null;
            connected = false;
            StatusText.Text = "Socket is disconnected.";
        }

        /// <summary>
        /// 公用按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void COMMON_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null)
            {
                return;
            }
            string content = btn.Content.ToString();

        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="sData"></param>
        private async void SendMsg(byte[] sData)
        {
            if (!connected)
            {
                StatusText.Text = "Must be connected to send!";
                return;
            }

            try
            {
                //StatusText.Text = "Trying to send data ...";

                byte[] sendData = ConvSendMsg(sData, 0);
                DataWriter writer = new DataWriter(clientSocket.OutputStream);
                //把数据写入到发送流
                writer.WriteBytes(sendData);
                //异步发送
                await writer.StoreAsync();

                //显示发送的消息内容
                StatusText.Text = System.Text.UnicodeEncoding.UTF8.GetString(sendData, 40, sData.Length) + " was sent";

                // detach the stream and close it
                writer.DetachStream();
                writer.Dispose();

            }
            catch (Exception exception)
            {
                // If this is an unknown status, 
                // it means that the error is fatal and retry will likely fail.
                if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
                {
                    throw;
                }
                StatusText.Text = "Send data or receive failed with error: " + exception.Message;
                // Could retry the connection, but for this simple example
                // just close the socket.
                closing = true;
                clientSocket.Dispose();
                clientSocket = null;
                connected = false;
            }
        }

        /// <summary>
        /// 封装发送的数据包
        /// </summary>
        /// <param name="sendData">数据内容</param>
        /// <param name="dataLength">数据长度</param>
        /// <param name="msgID">msgID</param>
        /// <returns>返回封装好的数据包</returns>
        private byte[] ConvSendMsg(byte[] sendData, int msgID, Int64 msgType = 1)
        {
            //数据包格式为 数据大小 + msgID + type + 保留字段 + 保留字段 + 用户自定义数据 + 数据内容

            // 0-3  字节，  unsigned int 代表数据大小
            int dataSize = sendData == null ? 0 : sendData.Length + 1;
            byte[] bLength = System.BitConverter.GetBytes(dataSize);

            // 4-7  字节，  int          代表msgID
            byte[] bID = System.BitConverter.GetBytes(msgID);

            // 8-15 字节，  long long    代表type
            byte[] bType = System.BitConverter.GetBytes(msgType);

            // 16-23字节，  long long    目前保留，准备用于时间戳
            // 24-31字节，  long long    目前保留，准备用于时间戳
            // 32-39字节，  long long    用户可以自定义的8字节数据
            byte[] bReserved = new byte[24];

            //组装数据包`
            byte[] b = new byte[dataSize + 40];
            Array.Copy(bLength, 0, b, 0, 4);
            Array.Copy(bID, 0, b, 4, 4);
            Array.Copy(bType, 0, b, 8, 8);
            if (sendData != null)
            {
                Array.Copy(sendData, 0, b, 40, sendData.Length);
            }
            return b;
        }

        /// <summary>
        /// 接收数据
        /// </summary>
        /// <returns></returns>
        private async Task BeginReceived()
        {

            //绑定已连接的StreamSocket到DataReader
            DataReader reader = new DataReader(clientSocket.InputStream);
            while (true)
            {
                try
                {
                    byte[] tempByteArr;

                    //获取数据长度
                    tempByteArr = new byte[4];
                    await reader.LoadAsync(sizeof(uint));
                    reader.ReadBytes(tempByteArr);
                    uint dataLength = System.BitConverter.ToUInt32(tempByteArr, 0);
                    //StatusText.Text = dataLength.ToString();

                    //获取msgID
                    tempByteArr = new byte[4];
                    await reader.LoadAsync(sizeof(int));
                    reader.ReadBytes(tempByteArr);
                    int msgID = System.BitConverter.ToInt32(tempByteArr, 0);

                    //读完数据头
                    tempByteArr = new byte[32];
                    await reader.LoadAsync(32);
                    reader.ReadBytes(tempByteArr);

                    if (dataLength > 0)
                    {
                        //读取数据正文
                        tempByteArr = new byte[dataLength];
                        await reader.LoadAsync(dataLength);
                        reader.ReadBytes(tempByteArr);
                        RecvMsg.Text = System.Text.UnicodeEncoding.UTF8.GetString(tempByteArr, 0, int.Parse(dataLength.ToString()));
                    }
                    else
                    {
                        RecvMsg.Text = "";
                    }
                }
                catch (Exception exception)
                {
                    // If this is an unknown status, 
                    // it means that the error is fatal and retry will likely fail.
                    if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
                    {
                        throw;
                    }

                    StatusText.Text = "Send data or receive failed with error: " + exception.Message;
                    // Could retry the connection, but for this simple example
                    // just close the socket.

                    closing = true;
                    clientSocket.Dispose();
                    clientSocket = null;
                    connected = false;
                }
            }
        }

        /// <summary>
        /// 发送用户输入参数
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendPm_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            int i = Grid.GetRow(btn);
            TextBox commandTB = (TextBox)SendPmGrid.FindName("command" + i.ToString());
            byte[] Pm = System.Text.UnicodeEncoding.UTF8.GetBytes(commandTB.Text);
            SendMsg(Pm);
        }
    }
}
