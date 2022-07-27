﻿namespace Wu.CommTool.ViewModels.DesignViewModels
{
    public class MqttServerDesignViewModel : MqttServerViewModel
    {
        private static MqttServerDesignViewModel _Instance;
        public static MqttServerDesignViewModel Instance => _Instance ??= new();
        public MqttServerDesignViewModel()
        {
            IsDrawersOpen.IsLeftDrawerOpen = true;
        }
    }
}