﻿using NetworkSocket.Core;
using NetworkSocket.Exceptions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkSocket.Core
{
    /// <summary>
    /// 定义任务控制行为接口
    /// </summary>
    internal interface ITaskSetAction
    {
        /// <summary>
        /// 获取任务的返回值类型
        /// </summary>
        Type ValueType { get; }

        /// <summary>
        /// 设置任务的行为结果
        /// </summary>     
        /// <param name="value">数据值</param>   
        /// <returns></returns>
        bool SetResult(object value);

        /// <summary>
        /// 设置设置为异常
        /// </summary>
        /// <param name="ex">异常</param>
        /// <returns></returns>
        bool SetException(Exception ex);
    }
}
