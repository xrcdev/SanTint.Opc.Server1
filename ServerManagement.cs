using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SanTint.Opc.Server
{
    internal class ServerManagement
    {
        static HttpServer _apiService = default;
        static OpcServerApp _opcServerApp = default;
        public static void Start()
        {
            SanTint.Common.Log.Logger.SoftWareName = "SanTint.Opc.Server";

            DBHelper.CheckOrInitSqliteDb();

            _apiService = new HttpServer();
            _apiService.Start();

            //_opcServerApp = new OpcServerApp();
            //_opcServerApp.Start();
        }

        public static void Stop()
        {
            _apiService?.Stop();
            _opcServerApp?.Stop();
        }
    }
}
