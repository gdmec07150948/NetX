﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Netx.Actor;
using Netx.Loggine;
using System;
using System.Threading.Tasks;
using ZYSocket;
using ZYSocket.FiberStream;
using ZYSocket.Interface;

namespace Netx.Service
{
    public abstract class ServiceBase
    {
        /// <summary>
        /// 日记工厂类
        /// </summary>
        protected ILoggerFactory LoggerFactory { get; set; }
        /// <summary>
        /// 日记输出
        /// </summary>
        protected ILog Log { get; private set; }


        protected IServiceProvider Container { get; }

        /// <summary>
        /// 服务器配置
        /// </summary>
        protected ServiceOption ServiceOption { get; }

        public string OpenKey { get => ServiceOption.VerifyKey; }


        public ServiceBase(IServiceProvider container)
        {
            LoggerFactory = container.GetRequiredService<ILoggerFactory>();
            Log = new DefaultLog(LoggerFactory.CreateLogger("NetxService"));
            SerializationPacker.Serialization = container.GetRequiredService<ISerialization>();
            Container = container;

            var actor_run = container.GetRequiredService<ActorRun>();
            foreach (var @event in container.GetServices<ActorEventBase>())            
                actor_run.CompletedEvent += @event.ActorEventCompleted;

            ServiceOption = container.GetRequiredService<IOptions<ServiceOption>>().Value;
        }


        protected Task SendToKeyError(IFiberRw fiberRw, bool iserr = false, string msg = "success")
        {
            using (var wrtokenerr = new WriteBytes(fiberRw))
            {
                wrtokenerr.WriteLen();
                wrtokenerr.Cmd(1000);
                wrtokenerr.Write(iserr);
                wrtokenerr.Write(msg);
                return wrtokenerr.Flush();
            }
        }

        protected Task SendToMessage(IFiberRw fiberRw, string msg)
        {
            using (var wrtokenerr = new WriteBytes(fiberRw))
            {
                wrtokenerr.WriteLen();
                wrtokenerr.Cmd(1001);
                wrtokenerr.Write(msg);
                return wrtokenerr.Flush();
            }
        }
    }
}
