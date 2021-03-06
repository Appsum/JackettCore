﻿using System.Threading;

namespace JackettCore.Services
{
    public interface IRunTimeService
    {
        void Spin();
    }

    class RunTimeService : IRunTimeService
    {
        private bool isRunning = true;

        public void Spin()
        {
            while (isRunning)
            {
                Thread.Sleep(2000);
            }
        }
    }
}
