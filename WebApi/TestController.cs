using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace SanTint.Opc.Server
{
    /// <summary>
    /// 用于测试接口是否正常
    /// </summary>
    public class TestController : ApiController
    {
        [HttpGet]
        public String GetString()
        {
            string HostName = Dns.GetHostName();
            string IP;
            IP = GetLocalIPAddress();

            List<string> data = new List<string>();
            Dictionary<string, string> dData = new Dictionary<string, string>();
            dData.Add("HostName", HostName);
            dData.Add("Ip", IP);
            data.Add(HostName);
            data.Add(IP);

            string json = JsonConvert.SerializeObject(dData);
            return json;
        }


        public static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }

    }
}
