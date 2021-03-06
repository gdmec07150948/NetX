﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Netx.Actor
{
    public interface IActorMessage
    {
         long Id { get; }

         int Cmd { get; }       

         object[] Args { get; }

         long PushTime { get; set; }
    
         long CompleteTime { get; set; }
     
    }

    public class ActorMessage<T>: IActorMessage
    {
        public long Id { get; }

        public int Cmd { get; }

        public object[] Args { get; }

        public long PushTime { get; set; }

        public long CompleteTime { get; set; }

        internal OpenAccess Access { get; }

        internal ActorResultAwaiter<T> Awaiter { get; }

        public ActorMessage(long id, int cmd, OpenAccess access, object[] args)            
        {
            Id = id;
            Cmd = cmd;
            Args = args;
            PushTime = TimeHelper.GetTime();
            CompleteTime = 0;
            this.Access = access;
            Awaiter = new ActorResultAwaiter<T>();
        }
    }
}
