using Newtonsoft.Json;

using SanTint.Opc.Server.Model;

using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SanTint.Opc.Server
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        static void Main()
        {
            //TODO:发布时需要注释掉
            //ServerManagement.Start();
            //var quitEvent = CtrlCHandler();
            //quitEvent.WaitOne(-1);

            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new Service1()
            };
            ServiceBase.Run(ServicesToRun);
        }

        private static ManualResetEvent CtrlCHandler()
        {
            var quitEvent = new ManualResetEvent(false);
            try
            {
                Console.CancelKeyPress += (_, eArgs) =>
                {
                    quitEvent.Set();
                    eArgs.Cancel = true;
                };
            }
            catch
            {
                // intentionally left blank
            }

            return quitEvent;
        }


        private static void GetTestData()
        {
            var aduReceived = new ADUReceived
            {
                ProcessOrder = "order1",
                DateTime = "",
                MaterialCode = "1234",
                ActualQuantity = 50,
                QuantityUOM = "KG",
                Lot = "123",
                PlantCode = "plant1",
                DeviceIdentifier = "device1",
                VesselID = "vessel1",
                ItemMaterial = 5678,
                BatchStepID = 987,
                Watchdog = true,
                ReadyForNewDosing = false,
                DosingCompleted = true,
                RequestAccepted = true,
                ID = -1,
                DataIdentifier = Guid.NewGuid().ToString(),
                Quantity = 60,
                IsComplete = true
            };

            var json1 = JsonConvert.SerializeObject(aduReceived);
        }
    }
}
