﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Netx.Loggine;

namespace Netx.Actor
{
    public class Actor<R> : IActor<R> where R:class
    {
        public const int Idle = 0;
        public const int Open = 1;
        public const int Disposed = 2;


        public ActorScheduler ActorScheduler { get; }

        public IServiceProvider Container { get; }

        public ActorController @ActorController { get; }

        public Dictionary<int,MethodRegister> CmdDict { get; }

        private readonly Lazy<ConcurrentQueue<ActorMessage<R>>> actorRunQueue;

        public ConcurrentQueue<ActorMessage<R>> ActorRunQueue { get => actorRunQueue.Value; }

        public ILog Log { get; }

        public int status = Idle;

        public IActorGet ActorGet { get; }

        public int Status => status;

        public int QueueCount => ActorRunQueue.Count;

        public ActorOptionAttribute Option { get; }

        internal event EventHandler<ActorMessage> CompletedEvent;



        public Actor(IServiceProvider container, IActorGet actorGet, ActorScheduler actorScheduler, ActorController instance)
        {
            this.ActorScheduler = actorScheduler;
            this.ActorGet = actorGet;
            this.ActorController = instance;

            var options= instance.GetType().GetCustomAttributes(typeof(ActorOptionAttribute), false);

            if (options != null) {
                foreach (var attr in options)
                    if (attr is ActorOptionAttribute option)
                        Option = option;
            }
            else
                Option = new ActorOptionAttribute();
            
           
            
            ActorController.ActorGet = ActorGet;
            ActorController.Status = this;
            this.Container = container;
           
            actorRunQueue = new Lazy<ConcurrentQueue<ActorMessage<R>>>();
            this.CmdDict = LoadRegister(instance.GetType());
            Log = new DefaultLog(container.GetRequiredService<ILoggerFactory>().CreateLogger($"Actor-{instance.GetType().Name}"));
        }


        private Dictionary<int, MethodRegister> LoadRegister(Type instanceType)
        {
            Dictionary<int, MethodRegister> registerdict = new Dictionary<int, MethodRegister>();

            var methods = instanceType.GetMethods();
            foreach (var method in methods)
                if (method.IsPublic)
                    foreach (var attr in method.GetCustomAttributes(typeof(TAG), true))
                        if (attr is TAG attrcmdtype)
                        {
                            if (TypeHelper.IsTypeOfBaseTypeIs(method.ReturnType, typeof(Task)) || method.ReturnType == typeof(void) || method.ReturnType == null)
                            {
                                var sr = new MethodRegister(instanceType, method);

                                if (!registerdict.ContainsKey(attrcmdtype.CmdTag))
                                    registerdict.Add(attrcmdtype.CmdTag, sr);
                                else
                                {
                                    Log.Error($"Register actor service {method.Name},cmd:{attrcmdtype.CmdTag} repeat");
                                    registerdict[attrcmdtype.CmdTag] = sr;
                                }
                            }
                            else
                                Log.Error($"Register Actor Service Return Type Err:{method.Name},Use void, Task or Task<T>");
                        }

            return registerdict;
        }




        public void Action(long id, int cmd, params object[] args)
        {

            if (status == Disposed)
                throw new ObjectDisposedException("this actor is dispose");

            if (Option?.MaxQueueCount > 0)
                if (ActorRunQueue.Count > Option.MaxQueueCount)
                    throw new NetxException($"this actor queue count >{Option.MaxQueueCount}", ErrorType.ActorQueueMaxErr);

            var sa = new ActorMessage<R>(id, cmd, args);
            ActorRunQueue.Enqueue(sa);
            try
            {
                Runing().Wait();
            }
            catch (Exception er)
            {                
                Log.Error(er);
            }
        }

        public async ValueTask AsyncAction(long id, int cmd, params object[] args)
        {
            if (status == Disposed)
                throw new ObjectDisposedException("this actor is dispose");

            if (Option?.MaxQueueCount > 0)
                if (ActorRunQueue.Count > Option.MaxQueueCount)
                    throw new NetxException($"this actor queue count >{Option.MaxQueueCount}", ErrorType.ActorQueueMaxErr);

            var sa = new ActorMessage<R>(id, cmd, args);
            var task = GetResult(sa);
            ActorRunQueue.Enqueue(sa);
            await Runing();

            if (sa.Awaiter.IsCompleted)
                return;
            else
                await task;
        }

        public async ValueTask<R> AsyncFunc(long id, int cmd, params object[] args)
        {
          
            if (status == Disposed)
                throw new ObjectDisposedException("this actor is dispose");

            if(Option?.MaxQueueCount>0)
                if(ActorRunQueue.Count>Option.MaxQueueCount)
                    throw new NetxException($"this actor queue count >{Option.MaxQueueCount}",ErrorType.ActorQueueMaxErr);

            var sa = new ActorMessage<R>(id, cmd, args);
            var task = GetResult(sa);
            ActorRunQueue.Enqueue(sa);

            await Runing();

            if (sa.Awaiter.IsCompleted)
                return sa.Awaiter.GetResult();
            else
                return await task;

          

        }


        private async Task<R> GetResult(ActorMessage<R> actorItem)
        {
            return await actorItem.Awaiter;
        }




        private Task Runing()
        {
            if (status == Disposed)
                throw new ObjectDisposedException("the Actor is Close");

            if (Interlocked.Exchange(ref status, Open) == Idle)
            {
                 async Task RunNext()
                 {
                    try
                    {
                        while (ActorRunQueue.TryDequeue(out ActorMessage<R> msg))
                        {

                            var res = await Call_runing(msg);

                            //当前容器的线程去触发外部事件已达到安全访问的目的,让用户自定义保存控制器中的数据和当前事件,已达到事件回溯
                            //请确保控制器里面的数据属性 是 {public get;private set;} 已保证在容器外的安全目的
                            CompletedEvent(ActorController, msg);

                            msg.Awaiter.Completed(res);


                            if (status == Disposed)
                                break;
                        }
                    }
                    finally
                    {
                        Interlocked.CompareExchange(ref status, Idle, Open);
                    }
                };

               return  ActorScheduler.Scheduler(RunNext);

            }

            return Task.CompletedTask;
        }

        private async Task<R> Call_runing(ActorMessage<R> result)
        {
            var cmd = result.Cmd;
            var args = result.Args;

            if (CmdDict.ContainsKey(cmd))
            {
                var service = CmdDict[cmd];

                if (service.ArgsLen == args.Length)
                {
                    ActorController.OrderTime = result.PushTime;

                    switch (service.ReturnMode)
                    {
                        case ReturnTypeMode.Null:
                            {
                                service.Method.Execute(ActorController, args.Length==0?null:args);
                                return null;
                            }                           
                        case ReturnTypeMode.Task:
                            {
                                await service.Method.ExecuteAsync(ActorController, args);
                                return null;
                            }                           
                        case ReturnTypeMode.TaskValue:
                            {
                                return (dynamic)  await service.Method.ExecuteAsync(ActorController, args);
                            }
                        default:
                            {
                                throw new NetxException("not find the return mode", ErrorType.ReturnModeErr);
                            }                            

                    }
                }
                else
                {
                    return (R)GetErrorResult($"actor cmd:{cmd} args count error", result.Id);                   
                }
            }
            else
            {
                return (R)GetErrorResult($"not find actor cmd:{cmd}", result.Id);              
            }           
        }

        public object GetErrorResult(string msg, long id)
        {
            Result err = new Result()
            {
                ErrorMsg = msg,
                Id = id,
                ErrorId = (int)ErrorType.ActorErr
            };

            return err;
        }


        public void Dispose()
        {
            if (Interlocked.Exchange(ref status, Disposed) != Disposed)
            {
                CmdDict.Clear();

                while (ActorRunQueue.Count>0)                
                    ActorRunQueue.TryDequeue(out _);                
            }           
        }
       
    }
}
