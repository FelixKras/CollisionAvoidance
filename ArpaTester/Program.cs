﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ArpaTester
{
    class Program
    {
        static void Main(string[] args)
        {
            if (true)
            {
                AutoResetEvent are = new AutoResetEvent(true);
                ArpaFromCamera.ArpaClass.Init();
                int ii = 10;
                while (ii-->0)
                {
                    are.WaitOne(1000);
                    var test=ArpaFromCamera.ArpaClass.GetArpa( 10);
                }
                
            }
            else
            {
                ArpaFromCamera.ArpaClass.Test();

            }


        }
    }
}
