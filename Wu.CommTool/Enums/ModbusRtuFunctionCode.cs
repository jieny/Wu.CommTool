﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wu.CommTool.Enums
{
    /// <summary>
    /// Modbus Rtu 的功能码和错误码
    /// </summary>
    public enum ModbusRtuFunctionCode
    {
        _0x01,//读线圈
        _0x81,//0x01的错误码

        _0x02,//读离散输入
        _0x82,//0x02的错误码

        _0x03,//读保持寄存器
        _0x83,//0x03的错误码

        _0x04,//读输入寄存器
        _0x84,//0x04的错误码

        _0x05,//写单个线圈
        _0x85,//0x85的错误码

        _0x06,//写单个寄存器
        _0x86,//0x06的错误码

        _0x0F,//15 写多个线圈
        _0x8F,//0x0F的错误码

        _0x10,//16 写多个寄存器
        _0x90,//0x10的错误码

        _0x14,//20 读文件记录
        _0x94,//0x14的错误码

        _0x15,//21 写文件记录
        _0x95,//0x15的错误码

        _0x16,//22 屏蔽写寄存器
        _0x96,//0x16的错误码

        _0x17,//23 读/写多个寄存器
        _0x97,//0x17的错误码

        _0x2B,//43 读设备识别码
        _0xAB,//0x2B的错误码
    }
}
