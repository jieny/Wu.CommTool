﻿using Prism.Ioc;
using System.Windows;
using Wu.CommTool.Common;
using Wu.CommTool.Dialogs.Views;
using Wu.CommTool.ViewModels;
using Wu.CommTool.ViewModels.DialogViewModels;
using Wu.CommTool.Views;

namespace Wu.CommTool
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            //注册自定义对话主机服务
            containerRegistry.Register<IDialogHostService, DialogHostService>();

            //注册页面
            containerRegistry.RegisterForNavigation<IndexView, IndexViewModel>();//首页
            containerRegistry.RegisterForNavigation<ModbusRtuAutoSearchDeviceView, ModbusRtuAutoSearchDeviceViewModel>();//消息提示窗口
            containerRegistry.RegisterForNavigation<ModbusRtuView, ModbusRtuViewModel>();//ModbusRtu
            containerRegistry.RegisterForNavigation<MsgView, MsgViewModel>();//消息提示窗口
            containerRegistry.RegisterForNavigation<MqttView, MqttViewModel>();//Mqtt
            containerRegistry.RegisterForNavigation<MqttServerView, MqttServerViewModel>();//MqttServer
        }

        /// <summary>
        /// 初始化完成
        /// </summary>
        protected override void OnInitialized()
        {
            //初始化窗口
            var service = App.Current.MainWindow.DataContext as IConfigureService;
            if (service != null)
                service.Congifure();
            base.OnInitialized();   
        }
    }
}