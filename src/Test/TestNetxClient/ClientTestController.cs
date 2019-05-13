﻿using Microsoft.Extensions.Logging;
using Netx;
using Netx.Loggine;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace TestNetxClient
{
    
    public class ClientTestController:MethodControllerBase  //or IMethodController
    {
        //public INetxSClient current { get; set; }

        //public T Get<T>() => current.Get<T>();


        [TAG(2000)]
        public  Task<int> AddOne(int a)
        {
            throw new Exception("Exception test");
            return Task.FromResult(++a);
        }

        [TAG(2001)]
        public Task<int> Add(int a,int b)
        {
            ILog log =new DefaultLog(Current.GetLogger("Add"));
            
            log.Info($"server request {a}+{b}");
            return Task.FromResult(a+b);
        }


        /// <summary>
        /// 递归测试
        /// </summary>
        /// <param name="a"></param>
        /// <returns></returns>
        [TAG(2002)]
        public Task<int> RecursiveTest(int a)
        {
            a--;
            if (a > 0)
                return Get<IServer>().RecursiveTest(a);
            else
                return Task.FromResult(a);
        }
    }
}
